using System.Numerics;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.StatusEffect;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Stories.CombatMech;

/// <summary>
/// Marker and configuration component for the RX47 Colonial Marines combat mech.
/// Owns mountable weapon slots, faceplate-driven pilot protection, step-stun and
/// barricade-bump damage profiles, health alert thresholds, and the body-overlay
/// visual stack used to render the helmet/arms above the seated pilot.
/// </summary>
/// <remarks>
/// Most behaviour is driven by <see cref="CombatMechSystem"/>; this component only
/// holds tunables and replicated runtime state. Pilot-side protection and visuals
/// live on <see cref="InsideCombatVehicleComponent"/>.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
[Access(typeof(CombatMechSystem))]
public sealed partial class CombatMechComponent : Component
{
    [DataField]
    public string EmptyWeaponState = "empty";

    [DataField]
    public string UnderbarrelSlot = "rmc-aslot-underbarrel";

    [DataField]
    public string GunMagazineContainerId = "gun_magazine";

    [DataField]
    public string GunChamberContainerId = "gun_chamber";

    [DataField]
    public string WeaponTankContainerId = "rx47_flamer_tank";

    [DataField]
    public string BodyOverlayContainerId = "rx47_body_overlay";

    [DataField(required: true)]
    public EntProtoId PrimaryWeapon;

    [DataField(required: true)]
    public EntProtoId SecondaryWeapon;

    // "empty" matches the EmptyWeaponState default; the runtime field is set explicitly when a weapon is mounted.
    [DataField, AutoNetworkedField]
    public string PrimaryWeaponState = "empty";

    [DataField, AutoNetworkedField]
    public string SecondaryWeaponState = "empty";

    [DataField, AutoNetworkedField]
    public bool HelmetClosed;

    [DataField, AutoNetworkedField]
    public string MarkingsColorState = string.Empty;

    [DataField, AutoNetworkedField]
    public string MarkingsSpecialtyState = string.Empty;

    [DataField, AutoNetworkedField]
    public bool HasTowLauncher;

    [DataField, AutoNetworkedField]
    public float MaxHealth = 3000f;

    [DataField]
    public float DamagedAlertThreshold = 25f;

    [DataField]
    public float CriticalAlertThreshold = 10f;

    [DataField]
    public TimeSpan WeaponInstallDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan WeaponDetachDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan ForceEjectDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public int DefaultWeaponEnsureMaxAttempts = 3;

    [DataField]
    public float BaseMoveDelay = 7f;

    [DataField]
    public float MinimumMoveDelay = 3f;

    [DataField]
    public float MoveDelayReductionPerSkill = 2f;

    [DataField]
    public TimeSpan StepStunDuration = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan StepStunCooldown = TimeSpan.FromSeconds(1);

    [DataField]
    public float StepDamage = 90f;

    [DataField]
    public EntityWhitelist StepTargetWhitelist = new()
    {
        Components = ["Marine"],
    };

    [DataField]
    public float StepStunOverlapRatio = 0.2f;

    [DataField]
    public TimeSpan StepActiveDuration = TimeSpan.FromSeconds(0.4);

    [DataField]
    public float BarricadeCollisionDamage = 900f;

    [DataField]
    public float BarricadeBumperRange = 0.5f;

    [DataField]
    public float BarricadeBumperProbeRadius = 0.5f;

    [DataField]
    public float BarricadeForwardDotMinimum = 0.35f;

    [DataField]
    public float WeaponDetachDropDistance = 1.1f;

    [DataField]
    public TimeSpan BarricadeBumperCooldown = TimeSpan.FromSeconds(0.25);

    [DataField]
    public Vector2 PilotVisualOffsetNorth = new(0f, 0.45f);

    [DataField]
    public Vector2 PilotVisualOffsetSouth = new(0f, 0.12f);

    [DataField]
    public Vector2 PilotVisualOffsetEastWest = new(0f, 0.28f);

    // Fallback only for vehicles without a Damageable damageContainer; RX47 damage forwarding
    // normally derives from the mounted mech's supported damage container types.
    [DataField]
    public List<ProtoId<DamageTypePrototype>> ForwardedDamageTypes = new()
    {
        "Blunt",
        "Slash",
        "Piercing",
        "Heat",
        "Shock",
        "Caustic",
    };

    [DataField]
    public HashSet<ProtoId<StatusEffectPrototype>> ProtectedStatusEffects = new()
    {
        "Blinded",
        "Dazed",
        "Drunk",
        "Flashed",
        "KnockedDown",
        "SlowedDown",
        "Stun",
    };

    [DataField]
    public EntProtoId<SkillDefinitionComponent> WeaponSkill = "RMCSkillPowerLoader";

    [DataField]
    public int WeaponSkillRequired = 3;

    [DataField]
    public EntProtoId<SkillDefinitionComponent> ForceEjectSkill = "RMCSkillFireman";

    [DataField]
    public int ForceEjectSkillRequired = 1;

    [DataField]
    public SoundSpecifier? EnterSound = new SoundPathSpecifier("/Audio/Mecha/sound_mecha_powerloader_step.ogg");

    [DataField]
    public SoundSpecifier? ExitSound = new SoundPathSpecifier("/Audio/Mecha/sound_mecha_powerloader_step.ogg");

    [DataField]
    public SoundSpecifier? DamageAlertSound = new SoundPathSpecifier("/Audio/Machines/warning_buzzer.ogg");

    // Runtime references, not safe across save/restart - intentionally not [DataField].
    [AutoNetworkedField]
    public EntityUid? PrimaryWeaponEntity;

    [AutoNetworkedField]
    public EntityUid? SecondaryWeaponEntity;

    [AutoNetworkedField]
    public EntityUid? PilotEntity;

    [DataField(required: true)]
    public EntProtoId BodyOverlayPrototype;

    [AutoNetworkedField]
    public EntityUid? BodyOverlayEntity;

    [DataField]
    public int PilotRenderOrder = 1;

    [ViewVariables]
    public bool DamageAlert25;

    [ViewVariables]
    public bool DamageAlert10;

    [ViewVariables]
    public TimeSpan NextBarricadeBumpAt;

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> NextStepStunAt = new();

    [ViewVariables]
    public TimeSpan LastStepMoveAt;

    [ViewVariables]
    public bool DefaultWeaponEnsureQueued;

    [ViewVariables]
    public int DefaultWeaponEnsureAttempts;

    [ViewVariables]
    public bool PrimaryWeaponInstallInProgress;

    [ViewVariables]
    public bool SecondaryWeaponInstallInProgress;

    // Throttle for OnMechMove-driven step-stun checks. ProcessMarineStepStuns covers the gap from Update.
    [ViewVariables]
    public TimeSpan NextStepStunCheckAt;
}

/// <summary>
/// Marks a weapon entity as an RX47 mountable module. Tracks the firing arc relative
/// to the mech's facing and the link back to the owning mech so the weapon can only
/// be fired by the seated pilot and within the allowed cone.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class CombatMechWeaponComponent : Component
{
    /// <summary>
    /// Full cone (degrees) centred on the mech facing in which this weapon is allowed to fire.
    /// Shots outside the arc are cancelled with the <c>stories-rx47-weapon-out-of-arc</c> popup.
    /// </summary>
    [DataField]
    public float FiringArc = 150f;

    /// <summary>
    /// Must match weapon_{armState}_{left/right} states in the RX47 visualizer.
    /// </summary>
    [DataField(required: true)]
    public string ArmState = string.Empty;

    // Valid only within a single round; not safe across save/restart.
    [AutoNetworkedField]
    public EntityUid? LinkedMech;
}

/// <summary>
/// Marks an attachable as an RX47 underbarrel. Underbarrel shots check that the
/// holder weapon is mounted on a mech and that the shooter is the current pilot,
/// preventing a looted weapon from being fired outside the mech.
/// </summary>
[RegisterComponent]
public sealed partial class CombatMechUnderbarrelComponent : Component;

/// <summary>
/// Multiplies the base melee damage of attacks against an RX47 mech. Used by RMC
/// anti-mech weapons (e.g. xeno ravager strikes) to scale up their effective damage
/// against vehicle armour without altering their damage against organic targets.
/// </summary>
[RegisterComponent]
public sealed partial class CombatMechMeleeDamageMultiplierComponent : Component
{
    /// <summary>
    /// Final damage multiplier applied on hit. <c>1.0</c> means no bonus; values
    /// less than or equal to 1 are skipped to avoid pointless work.
    /// </summary>
    [DataField(required: true)]
    public float Multiplier;
}

/// <summary>
/// Marks an entity as a valid target for the RX47 barricade-bumper damage pulse.
/// Used in addition to <c>BarricadeComponent</c> so non-barricade structures
/// (sandbags, tankfeed crates, etc.) can also opt in to being shoved aside.
/// </summary>
[RegisterComponent]
public sealed partial class CombatMechBumpDamageableComponent : Component;

/// <summary>
/// Marks a flamer-style RX47 weapon module that must keep its local fuel solution
/// in sync with the shared mech tank. The shot/ammo-count event handlers copy the
/// contents of the mounted weapon's tank into this attachable so vanilla flamer
/// code sees a single source of truth.
/// </summary>
// Not [NetworkedComponent]: fields are static container-ID strings; actual fuel state is
// tracked through SolutionContainerManager (which is networked) on the tank entity.
[RegisterComponent]
public sealed partial class CombatMechWeaponFlamerTankComponent : Component
{
    // Defaults mirror CombatMechComponent.WeaponTankContainerId / GunMagazineContainerId.
    // Duplicated literally because field initializers cannot reference instance members of another component.
    [DataField]
    public string WeaponTankContainerId = "rx47_flamer_tank";

    // Most RX47 flamers keep the local RMCFlamerTank directly on the attachable; this is only
    // a fallback for attachables that store their local tank in a container.
    [DataField]
    public string LocalTankContainerId = "gun_magazine";
}

/// <summary>
/// Pilot-side marker placed on a mob currently buckled into an RX47. Stores both the
/// vehicle link and a snapshot of mob-level components that are temporarily removed
/// while the pilot is sealed (infection susceptibility, weed effects, explosion stun)
/// so they can be restored on dismount without losing their RMC tuning.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class InsideCombatVehicleComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Vehicle;

    // Runtime delta flags - not DataField: prototyping or save/restore with these
    // set would produce incorrect RestorePilotProtection behaviour.
    [ViewVariables]
    public bool RemovedInfectable;

    [ViewVariables]
    public Dictionary<Sex, SoundSpecifier>? InfectableSound;

    [ViewVariables]
    public bool AddedUnparalyzable;

    [ViewVariables]
    public bool RemovedExplosionStun;

    [ViewVariables]
    public bool ExplosionStunWeak;

    [ViewVariables]
    public TimeSpan ExplosionStunBlindTime;

    [ViewVariables]
    public TimeSpan ExplosionStunBlurTime;

    [ViewVariables]
    public bool RemovedAffectableByWeeds;

    [ViewVariables]
    public bool OnXenoWeeds;

    [ViewVariables]
    public bool OnFriendlyWeeds;

    [ViewVariables]
    public bool OnXenoSlowResin;

    [ViewVariables]
    public bool OnXenoFastResin;

    [ViewVariables]
    public bool CollisionDisabled;

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> OpenFaceplateDamageAt = new();

    [ViewVariables]
    public Dictionary<string, CombatMechFixtureCollisionState> Fixtures = new();

}

[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct CombatMechFixtureCollisionState
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Mask { get; init; }

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Layer { get; init; }

    public CombatMechFixtureCollisionState(int mask, int layer)
    {
        Mask = mask;
        Layer = layer;
    }
}

/// <summary>
/// Identifies one of the two RX47 mountable weapon slots. Used throughout the system
/// in place of a <c>bool primary</c> flag so call sites read intent directly
/// (<c>WeaponSlot.Primary</c>) rather than guessing which boolean polarity means what.
/// </summary>
[Serializable, NetSerializable]
public enum WeaponSlot : byte
{
    Primary,
    Secondary,
}

[Serializable, NetSerializable]
public enum CombatMechVisuals : byte
{
    HelmetClosed,
    PrimaryWeapon,
    SecondaryWeapon,
    MarkingsColor,
    MarkingsSpecialty,
    HasTowLauncher,
}

[Serializable, NetSerializable]
public enum CombatMechVisualLayers : byte
{
    Legs,
    Body,
    Helmet,
    Arms,
    PrimaryWeapon,
    SecondaryWeapon,
    MarkingsColor,
    MarkingsSpecialty,
    TowLauncher,
}

[Serializable, NetSerializable]
public sealed partial class CombatMechInstallWeaponDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public WeaponSlot Slot;

    public override DoAfterEvent Clone() => new CombatMechInstallWeaponDoAfterEvent { Slot = Slot };

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is CombatMechInstallWeaponDoAfterEvent install && install.Slot == Slot;
    }
}

[Serializable, NetSerializable]
public sealed partial class CombatMechDetachWeaponDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public WeaponSlot Slot;

    public override DoAfterEvent Clone() => new CombatMechDetachWeaponDoAfterEvent { Slot = Slot };

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is CombatMechDetachWeaponDoAfterEvent detach && detach.Slot == Slot;
    }
}

[Serializable, NetSerializable]
public sealed partial class CombatMechForceEjectDoAfterEvent : SimpleDoAfterEvent;
