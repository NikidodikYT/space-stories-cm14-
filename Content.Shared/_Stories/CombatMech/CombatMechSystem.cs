using Content.Shared._RMC14.Atmos;
using Content.Shared._RMC14.CameraShake;
using Content.Shared._RMC14.Damage;
using Content.Shared._RMC14.Explosion;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Pulling;
using Content.Shared._RMC14.Sprite;
using Content.Shared._RMC14.Stun;
using Content.Shared._RMC14.Suicide;
using Content.Shared._RMC14.Weapons.Ranged;
using Content.Shared._RMC14.Weapons.Ranged.Flamer;
using Content.Shared._RMC14.Weapons.Ranged.IFF;
using Content.Shared._RMC14.Xenonids.Acid;
using Content.Shared._RMC14.Xenonids.Neurotoxin;
using Content.Shared.Atmos.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Camera;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.StatusEffect;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._Stories.CombatMech;

public sealed partial class CombatMechSystem : EntitySystem
{
    private const float ProtectionCleanupInterval = 0.25f;
    private const float PositionMoveEpsilon = 0.0001f;
    private const float DirectionEpsilon = 0.001f;
    private const float FiringTargetEpsilon = 0.01f;
    // Upper-bound the cadence of MoveEvent-driven step-stun probes; the periodic Update pass fills the gap.
    private static readonly TimeSpan StepStunMoveCheckInterval = TimeSpan.FromMilliseconds(100);
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private float _protectionCleanupAccumulator;
    // Scratch buffers used only inside Update's sequential server pass.
    private readonly HashSet<EntityUid> _contacts = new();
    private readonly HashSet<Entity<DamageOverTimeComponent>> _damageContacts = new();
    private readonly HashSet<EntityUid> _bumpDamageTargets = new();
    private readonly HashSet<EntityUid> _forceEjectingPilots = new();
    private readonly HashSet<EntityUid> _pilotsInCombatMechs = new();
    private readonly List<EntityUid> _staleDictionaryKeys = new();
    private readonly List<EntityUid> _stalePilots = new();
    // Entities that need default weapons spawned on the next tick (deferred past MapInit so hand containers exist).
    // Retries land in _nextTickDefaultWeapons so a failed attempt waits for the following Update pass instead of
    // re-running in the same tick (each tick gives GiveHands another chance to finish populating the mech).
    private readonly Queue<EntityUid> _pendingDefaultWeapons = new();
    private readonly Queue<EntityUid> _nextTickDefaultWeapons = new();
    // Snapshot buffer for safe iteration over _pilotsInCombatMechs while cleanup may reenter and mutate it.
    private readonly List<EntityUid> _pilotsIterBuffer = new();

    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedRMCFlammableSystem _flammable = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly RMCPullingSystem _rmcPulling = default!;
    [Dependency] private readonly SharedRMCSpriteSystem _rmcSprite = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly StandingStateSystem _standingState = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedStatusEffectsSystem _newStatusEffects = default!;
#pragma warning disable CS0618 // TODO RX47: migrate cleanup once RMC protected statuses move fully to StatusEffectNew.
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
#pragma warning restore CS0618
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    // Hot-path EntityQuery caches. Used inside MoveEvent / PreventCollideEvent handlers that may fire many times per tick.
    private EntityQuery<InputMoverComponent> _moverQuery;
    private EntityQuery<ItemComponent> _itemQuery;

    public override void Initialize()
    {
        _moverQuery = GetEntityQuery<InputMoverComponent>();
        _itemQuery = GetEntityQuery<ItemComponent>();

        SubscribeLocalEvent<CombatMechComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CombatMechComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<CombatMechComponent, UnstrapAttemptEvent>(OnUnstrapAttempt);
        SubscribeLocalEvent<CombatMechComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<CombatMechComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<CombatMechComponent, AfterAutoHandleStateEvent>(OnMechState);
        SubscribeLocalEvent<CombatMechComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<CombatMechComponent, GetIFFGunUserEvent>(OnGetIFFGunUser);
        SubscribeLocalEvent<CombatMechComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<CombatMechComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<CombatMechComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CombatMechComponent, DropAttemptEvent>(OnMechDropAttempt);
        SubscribeLocalEvent<CombatMechComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<CombatMechComponent, PickupAttemptEvent>(OnMechPickupAttempt);
        SubscribeLocalEvent<CombatMechComponent, StartCollideEvent>(OnMechStartCollide);
        SubscribeLocalEvent<CombatMechComponent, PreventCollideEvent>(OnMechPreventCollide);
        SubscribeLocalEvent<CombatMechComponent, MoveEvent>(OnMechMove);
        SubscribeLocalEvent<CombatMechComponent, AttemptMobCollideEvent>(OnMechAttemptMobCollide);
        SubscribeLocalEvent<CombatMechComponent, AttemptMobTargetCollideEvent>(OnMechAttemptMobTargetCollide);
        SubscribeLocalEvent<CombatMechComponent, GetSpeedModifierContactCapEvent>(OnMechGetSpeedModifierContactCap);
        SubscribeLocalEvent<CombatMechComponent, TileFrictionEvent>(OnMechTileFriction);
        SubscribeLocalEvent<CombatMechComponent, CombatMechInstallWeaponDoAfterEvent>(OnInstallWeaponDoAfter);
        SubscribeLocalEvent<CombatMechComponent, CombatMechDetachWeaponDoAfterEvent>(OnDetachWeaponDoAfter);
        SubscribeLocalEvent<CombatMechComponent, CombatMechForceEjectDoAfterEvent>(OnForceEjectDoAfter);
        SubscribeLocalEvent<CombatMechComponent, EntityTerminatingEvent>(OnMechTerminating);

        SubscribeLocalEvent<CombatMechMeleeDamageMultiplierComponent, MeleeHitEvent>(OnCombatMechMeleeHit);

        SubscribeLocalEvent<CombatMechWeaponComponent, AttemptShootEvent>(OnWeaponAttemptShoot, before: [typeof(SharedRMCFlamerSystem)]);
        SubscribeLocalEvent<CombatMechWeaponComponent, GetIFFGunUserEvent>(OnWeaponGetIFFGunUser);
        SubscribeLocalEvent<CombatMechWeaponComponent, ContainerIsRemovingAttemptEvent>(OnWeaponContainerRemoveAttempt);
        SubscribeLocalEvent<CombatMechWeaponComponent, InteractUsingEvent>(OnWeaponInteractUsing);
        SubscribeLocalEvent<CombatMechWeaponComponent, ItemSlotEjectAttemptEvent>(OnWeaponItemSlotEjectAttempt);
        SubscribeLocalEvent<CombatMechWeaponComponent, RMCTryAmmoEjectEvent>(OnWeaponTryAmmoEject);
        SubscribeLocalEvent<CombatMechWeaponComponent, GetVerbsEvent<AlternativeVerb>>(OnWeaponGetAlternativeVerbs);
        SubscribeLocalEvent<CombatMechUnderbarrelComponent, AttemptShootEvent>(OnMountedAttachableAttemptShoot, before: [typeof(SharedRMCFlamerSystem)]);
        SubscribeLocalEvent<CombatMechWeaponFlamerTankComponent, AttemptShootEvent>(OnWeaponFlamerAttemptShoot, before: [typeof(SharedRMCFlamerSystem)]);
        SubscribeLocalEvent<CombatMechWeaponFlamerTankComponent, GetAmmoCountEvent>(OnWeaponFlamerGetAmmoCount, before: [typeof(SharedRMCFlamerSystem)]);
        SubscribeLocalEvent<CombatMechWeaponFlamerTankComponent, GunShotEvent>(OnWeaponFlamerGunShot);
        SubscribeLocalEvent<RMCCameraShakingComponent, ComponentStartup>(OnCameraShakeStartup);
        SubscribeLocalEvent<InsideCombatVehicleComponent, AttackAttemptEvent>(OnInsideVehicleAttackAttempt);
        SubscribeLocalEvent<InsideCombatVehicleComponent, BeforeAttemptShootEvent>(OnInsideVehicleBeforeAttemptShoot);
        SubscribeLocalEvent<InsideCombatVehicleComponent, BeforeDamageChangedEvent>(OnInsideVehicleBeforeDamage);
        SubscribeLocalEvent<InsideCombatVehicleComponent, BeforeStatusEffectAddedEvent>(OnInsideVehicleBeforeStatusEffectAdded);
        SubscribeLocalEvent<InsideCombatVehicleComponent, CorrodingEvent>(OnInsideVehicleCorroding);
        SubscribeLocalEvent<InsideCombatVehicleComponent, DazedEvent>(OnInsideVehicleDazed);
        SubscribeLocalEvent<InsideCombatVehicleComponent, GetSpeedModifierContactCapEvent>(OnInsideVehicleGetSpeedModifierContactCap);
        SubscribeLocalEvent<InsideCombatVehicleComponent, KnockedDownEvent>(OnInsideVehicleKnockedDown);
        SubscribeLocalEvent<InsideCombatVehicleComponent, NeurotoxinInjectAttemptEvent>(OnInsideVehicleNeurotoxinInjectAttempt);
        SubscribeLocalEvent<InsideCombatVehicleComponent, StunnedEvent>(OnInsideVehicleStunned);
        SubscribeLocalEvent<InsideCombatVehicleComponent, GetIgnitionImmunityEvent>(OnInsideVehicleIgnitionImmunity);
        SubscribeLocalEvent<InsideCombatVehicleComponent, RMCGetFireImmunityEvent>(OnInsideVehicleFireImmunity);
        SubscribeLocalEvent<InsideCombatVehicleComponent, GetExplosionResistanceEvent>(OnInsideVehicleExplosionResistance);
        SubscribeLocalEvent<InsideCombatVehicleComponent, DropAttemptEvent>(OnInsideVehicleDropAttempt);
        SubscribeLocalEvent<InsideCombatVehicleComponent, PickupAttemptEvent>(OnInsideVehiclePickupAttempt);
        SubscribeLocalEvent<InsideCombatVehicleComponent, AfterAutoHandleStateEvent>(OnInsideVehicleState);
        SubscribeLocalEvent<InsideCombatVehicleComponent, ComponentShutdown>(OnInsideVehicleShutdown);
        SubscribeLocalEvent<InsideCombatVehicleComponent, MoveEvent>(OnInsideVehicleMove);
        SubscribeLocalEvent<InsideCombatVehicleComponent, GetEyeOffsetAttemptEvent>(OnInsideVehicleGetEyeOffsetAttempt);
        SubscribeLocalEvent<InsideCombatVehicleComponent, RMCSuicideDoAfterEvent>(
            OnInsideVehicleSuicideAttempt,
            before: [typeof(RMCSuicideSystem)]);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        while (_pendingDefaultWeapons.TryDequeue(out var pending))
        {
            if (Deleted(pending) || !TryComp(pending, out CombatMechComponent? mech))
                continue;

            mech.DefaultWeaponEnsureQueued = false;
            mech.DefaultWeaponEnsureAttempts++;
            var oldPrimary = mech.PrimaryWeaponEntity;
            var oldSecondary = mech.SecondaryWeaponEntity;
            var oldPrimaryState = mech.PrimaryWeaponState;
            var oldSecondaryState = mech.SecondaryWeaponState;

            var primaryReady = EnsureWeapon((pending, mech), true);
            var secondaryReady = EnsureWeapon((pending, mech), false);
            if (!primaryReady || !secondaryReady)
            {
                if (mech.DefaultWeaponEnsureAttempts < mech.DefaultWeaponEnsureMaxAttempts)
                {
                    mech.DefaultWeaponEnsureQueued = true;
                    _nextTickDefaultWeapons.Enqueue(pending);
                }
                else
                {
                    Log.Warning($"RX47 {ToPrettyString(pending)} failed to spawn default weapons after {mech.DefaultWeaponEnsureAttempts} attempts.");
                    mech.DefaultWeaponEnsureAttempts = 0;
                    mech.DefaultWeaponEnsureQueued = false;
                }
            }
            else
            {
                mech.DefaultWeaponEnsureAttempts = 0;
            }

            if (primaryReady && secondaryReady ||
                oldPrimary != mech.PrimaryWeaponEntity ||
                oldSecondary != mech.SecondaryWeaponEntity ||
                oldPrimaryState != mech.PrimaryWeaponState ||
                oldSecondaryState != mech.SecondaryWeaponState)
            {
                UpdateAppearance((pending, mech));
            }
        }

        while (_nextTickDefaultWeapons.TryDequeue(out var deferred))
            _pendingDefaultWeapons.Enqueue(deferred);

        _protectionCleanupAccumulator += frameTime;
        if (_protectionCleanupAccumulator < ProtectionCleanupInterval)
            return;

        _protectionCleanupAccumulator -= ProtectionCleanupInterval;

        ProcessBarricadeBumpers();
        ProcessMarineStepStuns();
        ProcessOpenFaceplateDamageOverTime();

        // ClearProtectedStatuses internally calls the legacy synchronous _statusEffects.TryRemoveStatusEffect,
        // whose subscribers may reenter and mutate _pilotsInCombatMechs (e.g. ejecting the pilot mid-cleanup).
        // Snapshot into a re-used buffer so the foreach never iterates the live HashSet.
        _pilotsIterBuffer.Clear();
        _pilotsIterBuffer.AddRange(_pilotsInCombatMechs);

        _stalePilots.Clear();
        foreach (var uid in _pilotsIterBuffer)
        {
            if (!TryComp(uid, out InsideCombatVehicleComponent? inside) ||
                !HasLiveVehicle((uid, inside)))
            {
                _stalePilots.Add(uid);
                continue;
            }

            if (!IsPilotSealed((uid, inside)))
                continue;

            // Most effects are blocked by events; this slower pass catches late-added components without ticking every frame.
            ClearProtectedStatuses((uid, inside));
            ClearProtectedMovementDebuffs((uid, inside));
            ClearProtectedOngoingEffects((uid, inside));
        }

        foreach (var uid in _stalePilots)
        {
            RemCompDeferred<InsideCombatVehicleComponent>(uid);
        }
    }

    private void OnMapInit(Entity<CombatMechComponent> ent, ref MapInitEvent args)
    {
        if (_net.IsClient)
        {
            UpdateAppearance(ent);
            return;
        }

        // EnsureBodyOverlay is called transitively by UpdateAppearance on the server.
        UpdateAppearance(ent);

        if (ent.Comp.DefaultWeaponEnsureQueued)
            return;

        // GiveHands finishes after MapInit; defer one tick so the mech hand containers exist before mounting weapons.
        ent.Comp.DefaultWeaponEnsureQueued = true;
        _pendingDefaultWeapons.Enqueue(ent.Owner);
    }

    private void EnsureBodyOverlay(Entity<CombatMechComponent> ent)
    {
        var proto = ent.Comp.BodyOverlayPrototype;
        if (string.IsNullOrEmpty(proto.Id))
            return;

        // Always ensure the slot exists so client replication has a stable destination for the overlay
        // entity and PVS late-joiners see it via container state instead of a stray transform.
        // ShowContents = true is mandatory: BaseContainer defaults hide the contained sprite from
        // rendering (the overlay literally disappears otherwise). OccludesLight = false because the
        // overlay is decorative and must not cast shadows onto the mech body underneath it.
        var slot = _container.EnsureContainer<ContainerSlot>(ent.Owner, ent.Comp.BodyOverlayContainerId, out var alreadyExisted);

        // Direct mutation of ContainerSlot fields does not mark the manager dirty, so PVS state would
        // ship the engine defaults (ShowContents=false) to clients and the overlay would render as
        // invisible. Always re-apply the desired settings and explicitly dirty the manager on changes.
        if (!alreadyExisted || slot.ShowContents != true || slot.OccludesLight != false)
        {
            slot.ShowContents = true;
            slot.OccludesLight = false;
            Dirty(ent.Owner, Comp<ContainerManagerComponent>(ent.Owner));
        }

        if (ent.Comp.BodyOverlayEntity is { } existing && !Deleted(existing))
        {
            if (slot.ContainedEntity == existing)
                return;

            // Old overlay drifted out of the slot (VV, another system, container reset). Delete it
            // before spawning the replacement so we do not leak orphan entities each respawn.
            QueueDel(existing);
        }

        var overlay = Spawn(proto, Transform(ent.Owner).Coordinates);
        if (!_container.Insert(overlay, slot))
        {
            QueueDel(overlay);
            return;
        }

        ent.Comp.BodyOverlayEntity = overlay;
        DirtyField(ent.Owner, ent.Comp, nameof(CombatMechComponent.BodyOverlayEntity));
        UpdateVisualStack(ent);
    }

}
