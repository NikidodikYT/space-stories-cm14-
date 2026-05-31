using System.Linq;
using Content.Shared._RMC14.CCVar;
using Content.Shared._RMC14.Xenonids.Announce;
using Content.Shared._RMC14.Xenonids.Egg;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Weeds;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Xenonids.Evolution;

public sealed class XenoEvolutionSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _action = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly SharedGameTicker _gameTicker = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedXenoAnnounceSystem _xenoAnnounce = default!;
    [Dependency] private readonly SharedXenoHiveSystem _xenoHive = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedXenoWeedsSystem _xenoWeeds = default!;
    [Dependency] private readonly IMapManager _map = default!;

    private TimeSpan _evolutionPointsRequireOvipositorAfter;
    private TimeSpan _evolutionAccumulatePointsBefore;
    private TimeSpan _evolveSameCasteCooldown;
    private TimeSpan _earlyEvoBoostBefore;

    // Stories-EvoQueue-Start
    private bool _evolutionQueueEnabled;
    private TimeSpan _evolutionQueueGrace;
    private TimeSpan _nextQueueUpdate;
    // Stories-EvoQueue-End

    private readonly HashSet<EntityUid> _climbable = new();
    private readonly HashSet<EntityUid> _doors = new();
    private readonly HashSet<EntityUid> _intersecting = new();

    private EntityQuery<MobStateComponent> _mobStateQuery;

    public override void Initialize()
    {
        _mobStateQuery = GetEntityQuery<MobStateComponent>();

        SubscribeLocalEvent<XenoDevolveComponent, XenoOpenDevolveActionEvent>(OnXenoOpenDevolveAction);

        SubscribeLocalEvent<XenoEvolutionComponent, MapInitEvent>(OnXenoEvolveMapInit);
        SubscribeLocalEvent<XenoEvolutionComponent, XenoOpenEvolutionsActionEvent>(OnXenoEvolveAction);
        SubscribeLocalEvent<XenoEvolutionComponent, XenoEvolutionDoAfterEvent>(OnXenoEvolveDoAfter);
        SubscribeLocalEvent<XenoEvolutionComponent, NewXenoEvolvedEvent>(OnXenoEvolutionNewEvolved);
        SubscribeLocalEvent<XenoEvolutionComponent, XenoDevolvedEvent>(OnXenoEvolutionDevolved);

        SubscribeLocalEvent<XenoNewlyEvolvedComponent, PreventCollideEvent>(OnNewlyEvolvedPreventCollide);

        SubscribeLocalEvent<XenoEvolutionGranterComponent, NewXenoEvolvedEvent>(OnGranterEvolved);

        SubscribeLocalEvent<XenoOvipositorChangedEvent>(OnOvipositorChanged);

        Subs.BuiEvents<XenoEvolutionComponent>(XenoEvolutionUIKey.Key,
            subs =>
            {
                subs.Event<XenoEvolveBuiMsg>(OnXenoEvolveBui);
                subs.Event<XenoStrainBuiMsg>(OnXenoStrainBui);
                subs.Event<XenoEvolutionQueueCancelBuiMsg>(OnXenoEvolutionQueueCancelBui); // Stories-EvoQueue
            });

        Subs.BuiEvents<XenoDevolveComponent>(XenoDevolveUIKey.Key,
            subs =>
            {
                subs.Event<XenoDevolveBuiMsg>(OnXenoDevolveBui);
            });

        Subs.CVar(_config, RMCCVars.RMCEvolutionPointsRequireOvipositorMinutes, v => _evolutionPointsRequireOvipositorAfter = TimeSpan.FromMinutes(v), true);
        Subs.CVar(_config, RMCCVars.RMCEvolutionPointsAccumulateBeforeMinutes, v => _evolutionAccumulatePointsBefore = TimeSpan.FromMinutes(v), true);
        Subs.CVar(_config, RMCCVars.RMCXenoEvolveSameCasteCooldownSeconds, v => _evolveSameCasteCooldown = TimeSpan.FromSeconds(v), true);
        Subs.CVar(_config, RMCCVars.RMCXenoEarlyEvoPointBoostBeforeMinutes, v => _earlyEvoBoostBefore = TimeSpan.FromMinutes(v), true);
        // Stories-EvoQueue-Start
        Subs.CVar(_config, RMCCVars.RMCXenoEvolutionQueueEnabled, v => _evolutionQueueEnabled = v, true);
        Subs.CVar(_config, RMCCVars.RMCXenoEvolutionQueueGraceSeconds, v => _evolutionQueueGrace = TimeSpan.FromSeconds(v), true);
        // Stories-EvoQueue-End
    }

    private void OnXenoOpenDevolveAction(Entity<XenoDevolveComponent> xeno, ref XenoOpenDevolveActionEvent args)
    {
        if (args.Handled)
            return;

        if (!DamagedCheckPopup(xeno))
            return;

        args.Handled = true;
        _ui.OpenUi(xeno.Owner, XenoDevolveUIKey.Key, xeno);
    }

    private void OnXenoEvolveMapInit(Entity<XenoEvolutionComponent> ent, ref MapInitEvent args)
    {
        _action.AddAction(ent, ref ent.Comp.Action, ent.Comp.ActionId);

        // Stories-EvoQueue: record tier-entry time for evolution-queue priority.
        if (_net.IsServer)
            EnsureComp<XenoEvolutionQueueComponent>(ent).TierEnteredAt = _gameTicker.RoundDuration();
    }

    private void OnXenoEvolveAction(Entity<XenoEvolutionComponent> xeno, ref XenoOpenEvolutionsActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.OpenUi(xeno.Owner, XenoEvolutionUIKey.Key, xeno);
        _ui.SetUiState(xeno.Owner, XenoEvolutionUIKey.Key, BuildEvolveState(xeno)); // Stories-EvoQueue
    }

    private void OnXenoEvolveBui(Entity<XenoEvolutionComponent> xeno, ref XenoEvolveBuiMsg args)
    {
        var actor = args.Actor;
        _ui.CloseUi(xeno.Owner, XenoEvolutionUIKey.Key, actor);

        if (_net.IsClient)
            return;

        if (!CanEvolvePopup(xeno, args.Choice))
        {
            Log.Warning($"{ToPrettyString(actor)} sent an invalid evolution choice: {args.Choice}.");
            return;
        }

        if (!DamagedCheckPopup(xeno, false))
            return;

        var time = _timing.CurTime;
        if (_prototypes.TryIndex(args.Choice, out var choice) &&
            choice.HasComponent<XenoEvolutionGranterComponent>(_compFactory) &&
            _xenoHive.GetHive(xeno.Owner) is { } hive &&
            hive.Comp.LastQueenDeath is { } lastQueenDeath &&
            time < lastQueenDeath + hive.Comp.NewQueenCooldown)
        {
            var left = lastQueenDeath + hive.Comp.NewQueenCooldown - time;
            var msg = Loc.GetString("rmc-xeno-evolution-cant-evolve-recent-queen-death-minutes",
                ("minutes", left.Minutes),
                ("seconds", left.Seconds));
            if (left.Minutes == 0)
            {
                msg = Loc.GetString("rmc-xeno-evolution-cant-evolve-recent-queen-death-seconds",
                    ("seconds", left.Seconds));
            }

            _popup.PopupEntity(msg, xeno, xeno, PopupType.MediumCaution);
            return;
        }

        var ev = new XenoEvolutionDoAfterEvent(args.Choice);
        var doAfter = new DoAfterArgs(EntityManager, xeno, xeno.Comp.EvolutionDelay, ev, xeno)
        {
            BreakOnRest = false,
        };

        if (xeno.Comp.EvolutionDelay > TimeSpan.Zero)
            _popup.PopupClient(Loc.GetString("cm-xeno-evolution-start"), xeno, xeno);

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            _jitter.DoJitter(xeno, xeno.Comp.EvolutionDelay, true, 80, 8, true);

            var popupOthers = Loc.GetString("rmc-xeno-evolution-start-others", ("xeno", xeno));
            _popup.PopupEntity(popupOthers, xeno, Filter.PvsExcept(xeno), true, PopupType.Medium);

            var popupSelf = Loc.GetString("rmc-xeno-evolution-start-self");
            _popup.PopupEntity(popupSelf, xeno, xeno, PopupType.Medium);
        }
    }

    private void OnXenoStrainBui(Entity<XenoEvolutionComponent> xeno, ref XenoStrainBuiMsg args)
    {
        var actor = args.Actor;
        _ui.CloseUi(xeno.Owner, XenoEvolutionUIKey.Key, actor);

        if (_net.IsClient)
            return;

        if (!xeno.Comp.Strains.Contains(args.Choice))
        {
            Log.Warning($"{ToPrettyString(actor)} sent an invalid strain choice: {args.Choice}.");
            return;
        }

        if (!ContainedCheckPopup(xeno))
            return;

        if (!DamagedCheckPopup(xeno, false))
            return;

        var newXeno = TransferXeno(xeno, args.Choice);
        var ev = new NewXenoEvolvedEvent(xeno, newXeno, false);
        RaiseLocalEvent(newXeno, ref ev, true);

        _adminLog.Add(LogType.RMCEvolve, $"Xenonid {ToPrettyString(xeno)} chose strain {ToPrettyString(newXeno)}");

        Del(xeno.Owner);

        var afterEv = new AfterNewXenoEvolvedEvent();
        RaiseLocalEvent(newXeno, ref afterEv);
    }

    // Stories-EvoQueue-Start
    private void OnXenoEvolutionQueueCancelBui(Entity<XenoEvolutionComponent> xeno, ref XenoEvolutionQueueCancelBuiMsg args)
    {
        if (_net.IsClient)
            return;

        if (!TryComp(xeno, out XenoEvolutionQueueComponent? queue) || queue.OfferedUntil == null)
            return;

        DeclineOffer((xeno.Owner, queue), Loc.GetString("rmc-xeno-queue-declined"));
        _ui.SetUiState(xeno.Owner, XenoEvolutionUIKey.Key, BuildEvolveState(xeno));
    }

    // Drops the active offer and sits the xeno out so the slot passes down the queue.
    private void DeclineOffer(Entity<XenoEvolutionQueueComponent> xeno, string? popup)
    {
        xeno.Comp.OfferedUntil = null;
        xeno.Comp.PassedUntil = _gameTicker.RoundDuration() + _evolutionQueueGrace;
        Dirty(xeno);

        if (popup != null)
            _popup.PopupEntity(popup, xeno, xeno, PopupType.MediumCaution);
    }
    // Stories-EvoQueue-End

    private void OnXenoDevolveBui(Entity<XenoDevolveComponent> xeno, ref XenoDevolveBuiMsg args)
    {
        _ui.CloseUi(xeno.Owner, XenoEvolutionUIKey.Key, xeno);
        TryDevolve(xeno, args.Choice);
    }

    private void OnXenoEvolveDoAfter(Entity<XenoEvolutionComponent> xeno, ref XenoEvolutionDoAfterEvent args)
    {
        if (_net.IsClient ||
            args.Handled ||
            args.Cancelled ||
            !_mind.TryGetMind(xeno, out _, out _) ||
            !CanEvolvePopup(xeno, args.Choice))
        {
            return;
        }

        args.Handled = true;

        var newXeno = TransferXeno(xeno, args.Choice);
        var ev = new NewXenoEvolvedEvent(xeno, newXeno, true);
        RaiseLocalEvent(newXeno, ref ev, true);

        _adminLog.Add(LogType.RMCEvolve, $"Xenonid {ToPrettyString(xeno)} evolved into {ToPrettyString(newXeno)}");

        Del(xeno.Owner);

        _popup.PopupEntity(Loc.GetString("cm-xeno-evolution-end"), newXeno, newXeno);

        var afterEv = new AfterNewXenoEvolvedEvent();
        RaiseLocalEvent(newXeno, ref afterEv);
    }

    private void OnXenoEvolutionNewEvolved(Entity<XenoEvolutionComponent> xeno, ref NewXenoEvolvedEvent args)
    {
        TransferPoints((args.OldXeno, args.OldXeno), xeno, args.SubtractPoints);
        _jitter.DoJitter(xeno, xeno.Comp.EvolutionJitterDuration, true, 80, 8, true);
    }

    private void OnXenoEvolutionDevolved(Entity<XenoEvolutionComponent> xeno, ref XenoDevolvedEvent args)
    {
        TransferPoints(args.OldXeno, (xeno, xeno), false);
    }

    private void TransferPoints(Entity<XenoEvolutionComponent?> old, Entity<XenoEvolutionComponent> xeno, bool subtract)
    {
        if (!Resolve(old, ref old.Comp, false))
            return;

        xeno.Comp.Points = subtract ? FixedPoint2.Max(0, old.Comp.Points - old.Comp.Max) : old.Comp.Points;

        Dirty(xeno);
    }

    private void OnNewlyEvolvedPreventCollide(Entity<XenoNewlyEvolvedComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.StopCollide.Contains(args.OtherEntity))
            args.Cancelled = true;
    }

    private void OnGranterEvolved(Entity<XenoEvolutionGranterComponent> ent, ref NewXenoEvolvedEvent args)
    {
        _xenoAnnounce.AnnounceSameHive(ent.Owner, Loc.GetString("rmc-new-queen"));
    }

    private void OnOvipositorChanged(ref XenoOvipositorChangedEvent ev)
    {
        if (_net.IsClient)
            return;

        var xenos = EntityQueryEnumerator<ActorComponent, XenoEvolutionComponent>();
        while (xenos.MoveNext(out var uid, out _, out var evo))
        {
            _ui.SetUiState(uid, XenoEvolutionUIKey.Key, BuildEvolveState((uid, evo))); // Stories-EvoQueue
        }
    }

    private bool ContainedCheckPopup(EntityUid xeno, bool doPopup = true)
    {
        if (!_container.IsEntityInContainer(xeno))
            return true;

        if (doPopup)
            _popup.PopupEntity(Loc.GetString("rmc-xeno-evolution-failed-bad-location"), xeno, xeno, PopupType.MediumCaution);

        return false;
    }

    private bool DamagedCheckPopup(EntityUid xeno, bool predicted = true, bool doPopup = true)
    {
        if (!TryComp(xeno, out DamageableComponent? damageable) ||
            damageable.TotalDamage <= 1)
            return true;

        // Stories-EvoQueue-Start
        if (doPopup)
        {
            if (predicted)
                _popup.PopupClient(Loc.GetString("rmc-xeno-evolution-cant-evolve-damaged"), xeno, xeno, PopupType.MediumCaution);
            else
                _popup.PopupEntity(Loc.GetString("rmc-xeno-evolution-cant-evolve-damaged"), xeno, xeno, PopupType.MediumCaution);
        }
        // Stories-EvoQueue-End

        return false;
    }

    private bool CanEvolvePopup(Entity<XenoEvolutionComponent> xeno, EntProtoId newXeno, bool doPopup = true)
    {
        if (!xeno.Comp.EvolvesTo.Contains(newXeno) && !xeno.Comp.EvolvesToWithoutPoints.Contains(newXeno))
            return false;

        if (!_prototypes.TryIndex(newXeno, out var prototype))
            return true;

        if (!ContainedCheckPopup(xeno, doPopup))
            return false;

        // TODO RMC14 revive jelly when added should not bring back dead queens
        if (prototype.TryGetComponent(out XenoEvolutionCappedComponent? capped, _compFactory) &&
            HasLiving<XenoEvolutionCappedComponent>(capped.Max, e => e.Comp.Id == capped.Id))
        {
            if (doPopup)
                _popup.PopupEntity(Loc.GetString("cm-xeno-evolution-failed-already-have", ("prototype", prototype.Name)), xeno, xeno, PopupType.MediumCaution);

            return false;
        }

        // TODO RMC14 only allow evolving towards Queen if none is alive
        if (!xeno.Comp.CanEvolveWithoutGranter && !HasLiving<XenoEvolutionGranterComponent>(1))
        {
            if (doPopup)
            {
                _popup.PopupEntity(
                    Loc.GetString("cm-xeno-evolution-failed-hive-shaken"),
                    xeno,
                    xeno,
                    PopupType.MediumCaution
                );
            }

            return false;
        }


        if (TryComp<RestrictEvolveOffWeedsComponent>(xeno.Owner, out var comp))
        {
            var coordinates = _transform.GetMoverCoordinates(xeno).SnapToGrid(EntityManager, _map);
            if (_transform.GetGrid(coordinates) is not { } gridUid ||
                !TryComp(gridUid, out MapGridComponent? grid))
            {
                return false;
            }

            if (!_xenoWeeds.IsOnWeeds((gridUid, grid), coordinates) && comp.RestrictTime > _gameTicker.RoundDuration())
            {
                // Stories-EvoQueue-Start
                if (doPopup)
                {
                    _popup.PopupEntity(
                        Loc.GetString("rmc-xeno-evolution-failed-early-weeds"),
                        xeno,
                        xeno,
                        PopupType.MediumCaution
                    );
                }
                // Stories-EvoQueue-End

                return false;
            }
        }

        prototype.TryGetComponent(out XenoComponent? newXenoComp, _compFactory);
        if (newXenoComp != null &&
            newXenoComp.UnlockAt > _gameTicker.RoundDuration())
        {
            if (doPopup)
            {
                _popup.PopupEntity(
                    Loc.GetString("cm-xeno-evolution-failed-cannot-support"),
                    xeno,
                    xeno,
                    PopupType.MediumCaution
                );
            }

            return false;
        }

        // Stories-EvoQueue-Start
        // Tier-limited castes are only reachable through the hive's evolution queue. An active offer
        // for this exact caste reserves a slot and lets the evolution through, skipping the cap below.
        var hasQueueOffer = false;
        if (_evolutionQueueEnabled &&
            newXenoComp != null &&
            _xenoHive.GetHive(xeno.Owner) is { } queueHive &&
            _xenoHive.IsQueuedCaste(queueHive, newXeno))
        {
            hasQueueOffer = TryComp(xeno, out XenoEvolutionQueueComponent? queue) &&
                            queue.OfferedUntil != null &&
                            queue.OfferedTier == newXenoComp.Tier;

            if (!hasQueueOffer)
            {
                if (doPopup)
                    _popup.PopupEntity(Loc.GetString("rmc-xeno-queue-wait"), xeno, xeno, PopupType.MediumCaution);

                return false;
            }
        }
        // Stories-EvoQueue-End

        if (newXenoComp != null &&
            !newXenoComp.BypassTierCount &&
            !hasQueueOffer && // Stories-EvoQueue: a queued offer reserves the slot, skip the live cap
            _xenoHive.GetHive(xeno.Owner) is { } oldHive &&
            _xenoHive.TryGetTierLimit((oldHive, oldHive.Comp), newXenoComp.Tier, out var limit))
        {
            _xenoHive.GetTierOccupancy(oldHive, newXenoComp.Tier, out var total, out var existing, out var slotCount);

            if (total != 0 && existing / (float) total >= limit && (!slotCount.ContainsKey(newXeno) || slotCount[newXeno] <= 0))
            {
                if (doPopup)
                {
                    _popup.PopupEntity(
                        Loc.GetString("cm-xeno-evolution-failed-hive-full", ("tier", newXenoComp.Tier)),
                        xeno,
                        xeno,
                        PopupType.MediumCaution
                    );
                }

                return false;
            }
        }

        if (TryComp(xeno, out XenoRecentlyDevolvedComponent? recently) &&
            recently.Recent.TryGetValue(newXeno, out var at) &&
            at + _evolveSameCasteCooldown > _timing.CurTime)
        {
            var timeRemaining = at + _evolveSameCasteCooldown - _timing.CurTime;
            var msg = Loc.GetString("rmc-xeno-evolution-cant-evolve-caste-cooldown",
                ("minutes", timeRemaining.Minutes),
                ("seconds", timeRemaining.Seconds));

            if (doPopup)
                _popup.PopupEntity(msg, xeno, xeno, PopupType.MediumCaution);

            return false;
        }

        return true;
    }

    private bool CanEvolveAny(Entity<XenoEvolutionComponent> xeno)
    {
        if (xeno.Comp.Points >= xeno.Comp.Max && xeno.Comp.EvolvesTo.Count > 0)
            return true;

        foreach (var evolution in xeno.Comp.EvolvesToWithoutPoints)
        {
            if (CanEvolvePopup(xeno, evolution, false))
                return true;
        }

        return false;
    }

    // TODO RMC14 make this a property of the hive component
    // TODO RMC14 per-hive
    public int GetLiving<T>(Predicate<Entity<T>>? predicate = null) where T : IComponent
    {
        var total = 0;
        var query = EntityQueryEnumerator<T>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_mobStateQuery.TryComp(uid, out var mobState) &&
                _mobState.IsDead(uid, mobState))
            {
                continue;
            }

            if (predicate != null && !predicate((uid, comp)))
                continue;

            total++;
        }

        return total;
    }

    // TODO RMC14 make this a property of the hive component
    // TODO RMC14 per-hive
    public bool HasLiving<T>(int count, Predicate<Entity<T>>? predicate = null) where T : IComponent
    {
        if (count <= 0)
            return true;

        var total = 0;
        var query = EntityQueryEnumerator<T>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_mobStateQuery.TryComp(uid, out var mobState) &&
                _mobState.IsDead(uid, mobState))
            {
                continue;
            }

            if (predicate != null && !predicate((uid, comp)))
                continue;

            total++;

            if (total >= count)
                return true;
        }

        return false;
    }

    public FixedPoint2 AddPointsCapped(Entity<XenoEvolutionComponent?> evolution, FixedPoint2 points)
    {
        if (!Resolve(evolution, ref evolution.Comp, false))
            return FixedPoint2.Zero;

        var oldPoints = evolution.Comp.Points;
        evolution.Comp.Points += FixedPoint2.Min(evolution.Comp.Max, points);
        Dirty(evolution);

        return evolution.Comp.Points - oldPoints;
    }

    public void SetPoints(Entity<XenoEvolutionComponent> evolution, FixedPoint2 points)
    {
        evolution.Comp.Points = points;
        Dirty(evolution);
    }

    public bool NeedsOvipositor()
    {
        return _gameTicker.RoundDuration() > _evolutionPointsRequireOvipositorAfter;
    }

    public bool HasOvipositor()
    {
        return HasLiving<XenoEvolutionGranterComponent>(1, e => HasComp<XenoAttachedOvipositorComponent>(e));
    }

    public bool LackingOvipositor()
    {
        return NeedsOvipositor() && !HasOvipositor();
    }

    private EntityUid TransferXeno(EntityUid xeno, EntProtoId proto)
    {
        var coordinates = _transform.GetMoverCoordinates(xeno);
        var newXeno = Spawn(proto, coordinates);
        _xenoHive.SetSameHive(xeno, newXeno);

        if (_mind.TryGetMind(xeno, out var mindId, out _))
        {
            _mind.TransferTo(mindId, newXeno);
            _mind.UnVisit(mindId);
        }

        foreach (var held in _hands.EnumerateHeld(xeno))
        {
            _hands.TryDrop(xeno, held);
        }

        // TODO RMC14 this is a hack because climbing on a newly created entity does not work properly for the client
        var comp = EnsureComp<XenoNewlyEvolvedComponent>(newXeno);

        _doors.Clear();
        _entityLookup.GetEntitiesIntersecting(xeno, _doors);
        foreach (var id in _doors)
        {
            if (HasComp<DoorComponent>(id) || HasComp<AirlockComponent>(id))
                comp.StopCollide.Add(id);
        }

        var newRecently = EnsureComp<XenoRecentlyDevolvedComponent>(newXeno);
        if (TryComp(xeno, out XenoRecentlyDevolvedComponent? oldRecently))
        {
            foreach (var (id, time) in oldRecently.Recent)
            {
                newRecently.Recent[id] = time;
            }
        }

        if (Prototype(xeno)?.ID is { } oldId)
            newRecently.Recent[oldId] = _timing.CurTime;

        return newXeno;
    }

    private void TryDevolve(Entity<XenoDevolveComponent> xeno, EntProtoId to, bool damagedCheck = true)
    {
        if (damagedCheck && !DamagedCheckPopup(xeno))
            return;

        if (Devolve(xeno, to) is { } newXeno && _net.IsServer)
            _popup.PopupEntity(Loc.GetString("rmc-xeno-evolution-devolve", ("xeno", newXeno)), newXeno, newXeno, PopupType.LargeCaution);
    }

    public EntityUid? Devolve(Entity<XenoDevolveComponent> xeno, EntProtoId to)
    {
        if (_net.IsClient ||
            !xeno.Comp.DevolvesTo.Contains(to))
        {
            return null;
        }

        var newXeno = TransferXeno(xeno, to);
        var ev = new XenoDevolvedEvent(xeno, newXeno);
        RaiseLocalEvent(newXeno, ref ev, true);

        _adminLog.Add(LogType.RMCDevolve, $"Xenonid {ToPrettyString(xeno)} devolved into {ToPrettyString(newXeno)}");

        Del(xeno.Owner);

        var afterEv = new AfterNewXenoEvolvedEvent();
        RaiseLocalEvent(newXeno, ref afterEv);

        return newXeno;
    }

    public override void Update(float frameTime)
    {
        var newly = EntityQueryEnumerator<XenoNewlyEvolvedComponent>();
        while (newly.MoveNext(out var uid, out var comp))
        {
            if (comp.TriedClimb)
            {
                _intersecting.Clear();
                _entityLookup.GetEntitiesIntersecting(uid, _intersecting);
                for (var i = comp.StopCollide.Count - 1; i >= 0; i--)
                {
                    var colliding = comp.StopCollide[i];
                    if (!_intersecting.Contains(colliding))
                        comp.StopCollide.RemoveAt(i);
                }

                if (comp.StopCollide.Count == 0)
                    RemCompDeferred<XenoNewlyEvolvedComponent>(uid);

                continue;
            }

            comp.TriedClimb = true;
            if (TryComp(uid, out ClimbingComponent? climbing))
            {
                _climbable.Clear();
                _entityLookup.GetEntitiesIntersecting(uid, _climbable);

                foreach (var intersecting in _climbable)
                {
                    if (HasComp<ClimbableComponent>(intersecting))
                    {
                        _climb.ForciblySetClimbing(uid, intersecting);
                        Dirty(uid, climbing);
                        break;
                    }
                }
            }
        }

        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var roundDuration = _gameTicker.RoundDuration();

        // Stories-EvoQueue-Start
        if (_evolutionQueueEnabled && time >= _nextQueueUpdate)
        {
            _nextQueueUpdate = time + TimeSpan.FromSeconds(1);
            UpdateEvolutionQueue(roundDuration);
        }
        // Stories-EvoQueue-End

        var needsOvipositor = NeedsOvipositor();
        var hasGranter = needsOvipositor
            ? HasOvipositor()
            : HasLiving<XenoEvolutionGranterComponent>(1);
        if (needsOvipositor)
        {
            var granters = EntityQueryEnumerator<XenoEvolutionGranterComponent>();
            while (granters.MoveNext(out var uid, out var granter))
            {
                if (granter.GotOvipositorPopup)
                    continue;

                granter.GotOvipositorPopup = true;
                Dirty(uid, granter);

                _popup.PopupEntity("It is time to settle down and let your children grow.",
                    uid,
                    uid,
                    PopupType.LargeCaution
                );

                _xenoHive.AnnounceNeedsOvipositorToSameHive(uid);
            }
        }

        var evoBonus = FixedPoint2.Zero;
        var bonuses = EntityQueryEnumerator<EvolutionBonusComponent>();
        while (bonuses.MoveNext(out var comp))
        {
            evoBonus += comp.Amount;
        }

        FixedPoint2? evoOverride = null;
        var overrides = EntityQueryEnumerator<EvolutionOverrideComponent>();
        while (overrides.MoveNext(out var comp))
        {
            evoOverride = comp.Amount;
        }

        var evolution = EntityQueryEnumerator<XenoEvolutionComponent>();
        while (evolution.MoveNext(out var uid, out var comp))
        {
            if (comp.Max == FixedPoint2.Zero)
                continue;

            if (time < comp.LastPointsAt + TimeSpan.FromSeconds(1))
                continue;

            comp.LastPointsAt = time;
            Dirty(uid, comp);

            if (!comp.GotPopup && CanEvolveAny((uid, comp)))
            {
                comp.GotPopup = true;
                Dirty(uid, comp);

                _popup.PopupEntity(Loc.GetString("cm-xeno-evolution-ready"), uid, uid, PopupType.Large);
                _audio.PlayEntity(comp.EvolutionReadySound, uid, uid);
                continue;
            }
            var points = (_earlyEvoBoostBefore > _gameTicker.RoundDuration()) ? comp.EarlyPointsPerSecond : comp.PointsPerSecond;
            var gain = evoOverride ?? points + evoBonus;
            if (comp.Points < comp.Max || roundDuration < _evolutionAccumulatePointsBefore)
            {
                if (needsOvipositor && comp.RequiresGranter && !hasGranter)
                    continue;

                SetPoints((uid, comp), comp.Points + gain);
            }
            else if (comp.Points > comp.Max)
            {
                SetPoints((uid, comp), FixedPoint2.Max(comp.Points - gain, comp.Max));
            }
        }
    }

    // Stories-EvoQueue-Start
    private XenoEvolveBuiState BuildEvolveState(Entity<XenoEvolutionComponent> xeno)
    {
        var queueChoices = new List<EntProtoId>();

        if (_evolutionQueueEnabled && _xenoHive.GetHive(xeno.Owner) is { } hive)
        {
            foreach (var choice in xeno.Comp.EvolvesTo)
            {
                if (_xenoHive.IsQueuedCaste(hive, choice))
                    queueChoices.Add(choice);
            }
        }

        return new XenoEvolveBuiState(LackingOvipositor(), queueChoices);
    }

    private bool TryGetCasteTier(EntProtoId caste, out int tier)
    {
        tier = 0;
        if (!_prototypes.TryIndex(caste, out var proto) ||
            !proto.TryGetComponent(out XenoComponent? xenoComp, _compFactory))
        {
            return false;
        }

        tier = xenoComp.Tier;
        return true;
    }

    // Whether the xeno can evolve into a queued caste of the given tier in its hive.
    private bool HasQueuedTargetOfTier(Entity<XenoEvolutionComponent> xeno, Entity<HiveComponent> hive, int tier)
    {
        foreach (var caste in xeno.Comp.EvolvesTo)
        {
            if (_xenoHive.IsQueuedCaste(hive, caste) && TryGetCasteTier(caste, out var t) && t == tier)
                return true;
        }

        return false;
    }

    // List 1, built on demand: living, player-attached, ready xenos eligible for a tier slot,
    // excluding those already offered (List 2) and those sitting out after a decline.
    private List<Entity<XenoEvolutionQueueComponent>> GetQueueCandidates(Entity<HiveComponent> hive, int tier, TimeSpan roundDuration)
    {
        var result = new List<Entity<XenoEvolutionQueueComponent>>();

        var query = EntityQueryEnumerator<XenoEvolutionQueueComponent, XenoEvolutionComponent, ActorComponent, HiveMemberComponent>();
        while (query.MoveNext(out var uid, out var queue, out var evo, out _, out var member))
        {
            if (member.Hive != hive.Owner ||
                queue.OfferedUntil != null ||
                (queue.PassedUntil is { } passed && passed > roundDuration) ||
                _mobState.IsDead(uid) ||
                evo.Points < evo.Max ||
                !HasQueuedTargetOfTier((uid, evo), hive, tier))
            {
                continue;
            }

            result.Add((uid, queue));
        }

        return result;
    }

    /// <summary>
    /// Continuously offers open tier slots to the longest-waiting living candidates (no opt-in, no lottery):
    /// expired or declined offers sit out and the slot passes down to the next candidate.
    /// </summary>
    private void UpdateEvolutionQueue(TimeSpan roundDuration)
    {
        var refresh = new HashSet<EntityUid>();

        // List 2 bookkeeping: drop invalid/expired offers, count those still reserving a slot per (hive, tier).
        var pending = new Dictionary<(EntityUid Hive, int Tier), int>();
        var offers = EntityQueryEnumerator<XenoEvolutionQueueComponent>();
        while (offers.MoveNext(out var uid, out var queue))
        {
            if (queue.OfferedUntil is not { } until)
                continue;

            if (_xenoHive.GetHive(uid) is not { } offerHive || _mobState.IsDead(uid) || !HasComp<ActorComponent>(uid))
            {
                queue.OfferedUntil = null;
                Dirty(uid, queue);
                continue;
            }

            if (until <= roundDuration)
            {
                DeclineOffer((uid, queue), Loc.GetString("rmc-xeno-queue-expired"));
                refresh.Add(offerHive.Owner);
                continue;
            }

            var key = (offerHive.Owner, queue.OfferedTier);
            pending[key] = pending.GetValueOrDefault(key) + 1;
        }

        // List 1: fill remaining open slots from the longest-waiting candidates.
        var hives = EntityQueryEnumerator<HiveComponent>();
        while (hives.MoveNext(out var hiveId, out var hiveComp))
        {
            var hive = new Entity<HiveComponent>(hiveId, hiveComp);
            foreach (var tier in hiveComp.TierLimits.Keys)
            {
                var need = _xenoHive.GetOpenTierSlots(hive, tier) - pending.GetValueOrDefault((hiveId, tier));
                if (need <= 0)
                    continue;

                var candidates = GetQueueCandidates(hive, tier, roundDuration);
                if (candidates.Count == 0)
                    continue;

                candidates.Sort((a, b) => a.Comp.TierEnteredAt.CompareTo(b.Comp.TierEnteredAt));

                foreach (var candidate in candidates)
                {
                    if (need <= 0)
                        break;

                    candidate.Comp.OfferedUntil = roundDuration + _evolutionQueueGrace;
                    candidate.Comp.OfferedTier = tier;
                    Dirty(candidate);
                    _popup.PopupEntity(
                        Loc.GetString("rmc-xeno-queue-offered", ("seconds", (int)_evolutionQueueGrace.TotalSeconds)),
                        candidate, candidate, PopupType.Large);
                    refresh.Add(hiveId);
                    need--;
                }
            }
        }

        foreach (var hiveId in refresh)
            RefreshHiveEvolutionUis(hiveId);
    }

    private void RefreshHiveEvolutionUis(EntityUid hiveId)
    {
        var xenos = EntityQueryEnumerator<ActorComponent, XenoEvolutionComponent, HiveMemberComponent>();
        while (xenos.MoveNext(out var uid, out _, out var evo, out var member))
        {
            if (member.Hive != hiveId)
                continue;

            _ui.SetUiState(uid, XenoEvolutionUIKey.Key, BuildEvolveState((uid, evo)));
        }
    }
    // Stories-EvoQueue-End
}
