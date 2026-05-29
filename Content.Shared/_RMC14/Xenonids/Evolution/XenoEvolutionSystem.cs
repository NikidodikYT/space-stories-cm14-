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
using Robust.Shared.Random; // Stories-Lottery
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
    [Dependency] private readonly IRobustRandom _random = default!; // Stories-Lottery
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
    private bool _tierLotteryEnabled; // Stories-Lottery

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
                subs.Event<XenoLotteryRegisterBuiMsg>(OnXenoLotteryRegisterBui); // Stories-Lottery
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
        Subs.CVar(_config, RMCCVars.RMCXenoTierLotteryEnabled, v => _tierLotteryEnabled = v, true); // Stories-Lottery
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
    }

    private void OnXenoEvolveAction(Entity<XenoEvolutionComponent> xeno, ref XenoOpenEvolutionsActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.OpenUi(xeno.Owner, XenoEvolutionUIKey.Key, xeno);
        _ui.SetUiState(xeno.Owner, XenoEvolutionUIKey.Key, BuildEvolveState(xeno)); // Stories-Lottery
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

    // Stories-Lottery-Start
    private void OnXenoLotteryRegisterBui(Entity<XenoEvolutionComponent> xeno, ref XenoLotteryRegisterBuiMsg args)
    {
        var actor = args.Actor;

        if (_net.IsClient)
            return;

        if (!xeno.Comp.EvolvesTo.Contains(args.Choice))
        {
            Log.Warning($"{ToPrettyString(actor)} sent an invalid lottery choice: {args.Choice}.");
            return;
        }

        // The window may have closed between the click and now (e.g. the draw just fired) - ignore quietly.
        if (!_tierLotteryEnabled || !IsLotteryOpenFor(xeno, args.Choice, out _))
            return;

        // Toggling the already-registered target cancels participation.
        if (TryComp(xeno, out XenoLotteryRegistrationComponent? registration) && registration.Target == args.Choice)
        {
            RemComp<XenoLotteryRegistrationComponent>(xeno);
            _popup.PopupEntity(Loc.GetString("rmc-xeno-lottery-cancelled"), xeno, xeno, PopupType.Medium);
        }
        else
        {
            // Same entry gate as a manual evolve - enough points and undamaged.
            if (xeno.Comp.Points < xeno.Comp.Max)
                return;

            if (!DamagedCheckPopup(xeno, predicted: false, doPopup: false))
            {
                _popup.PopupEntity(Loc.GetString("rmc-xeno-lottery-damaged"), xeno, xeno, PopupType.MediumCaution);
                return;
            }

            registration = EnsureComp<XenoLotteryRegistrationComponent>(xeno);
            registration.Target = args.Choice;
            Dirty(xeno, registration);

            var name = _prototypes.TryIndex(args.Choice, out var proto) ? proto.Name : args.Choice.Id;
            _popup.PopupEntity(Loc.GetString("rmc-xeno-lottery-registered", ("caste", name)), xeno, xeno, PopupType.Medium);
        }

        _ui.SetUiState(xeno.Owner, XenoEvolutionUIKey.Key, BuildEvolveState(xeno));
    }
    // Stories-Lottery-End

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

        EvolveXeno(xeno, args.Choice, subtractPoints: true, endPopup: true); // Stories-Lottery
    }

    // Stories-Lottery-Start
    /// <summary>
    /// Completes an evolution: spawns the new caste, transfers the mind, logs it, deletes the old xeno
    /// and raises the evolved events. Shared by the normal do-after path and the tier lottery.
    /// </summary>
    private EntityUid EvolveXeno(Entity<XenoEvolutionComponent> xeno, EntProtoId choice, bool subtractPoints, bool endPopup)
    {
        var newXeno = TransferXeno(xeno, choice);
        var ev = new NewXenoEvolvedEvent(xeno, newXeno, subtractPoints);
        RaiseLocalEvent(newXeno, ref ev, true);

        _adminLog.Add(LogType.RMCEvolve, $"Xenonid {ToPrettyString(xeno)} evolved into {ToPrettyString(newXeno)}");

        Del(xeno.Owner);

        if (endPopup)
            _popup.PopupEntity(Loc.GetString("cm-xeno-evolution-end"), newXeno, newXeno);

        var afterEv = new AfterNewXenoEvolvedEvent();
        RaiseLocalEvent(newXeno, ref afterEv);

        return newXeno;
    }
    // Stories-Lottery-End

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
            _ui.SetUiState(uid, XenoEvolutionUIKey.Key, BuildEvolveState((uid, evo))); // Stories-Lottery
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

        // Stories-Lottery-Start
        if (doPopup)
        {
            if (predicted)
                _popup.PopupClient(Loc.GetString("rmc-xeno-evolution-cant-evolve-damaged"), xeno, xeno, PopupType.MediumCaution);
            else
                _popup.PopupEntity(Loc.GetString("rmc-xeno-evolution-cant-evolve-damaged"), xeno, xeno, PopupType.MediumCaution);
        }
        // Stories-Lottery-End

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
                // Stories-Lottery-Start
                if (doPopup)
                {
                    _popup.PopupEntity(
                        Loc.GetString("rmc-xeno-evolution-failed-early-weeds"),
                        xeno,
                        xeno,
                        PopupType.MediumCaution
                    );
                }
                // Stories-Lottery-End

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

        // Stories-Lottery-Start
        // While a tier's first opening is still being raffled, the lottery is the only way in: block
        // the normal evolve path so it can't be raced for at the unlock tick.
        if (_tierLotteryEnabled &&
            newXenoComp != null &&
            _xenoHive.GetHive(xeno.Owner) is { } lotteryHive &&
            _xenoHive.IsLotteryCaste(lotteryHive, newXeno) &&
            _xenoHive.IsLotteryPending(lotteryHive, newXenoComp.Tier, out _))
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
        // Stories-Lottery-End

        if (newXenoComp != null &&
            !newXenoComp.BypassTierCount &&
            _xenoHive.GetHive(xeno.Owner) is { } oldHive &&
            _xenoHive.TryGetTierLimit((oldHive, oldHive.Comp), newXenoComp.Tier, out var limit))
        {
            _xenoHive.GetTierOccupancy(oldHive, newXenoComp.Tier, out var total, out var existing, out var slotCount); // Stories-Lottery

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

        // Stories-Lottery-Start
        if (_tierLotteryEnabled)
            UpdateLotteries(roundDuration);
        // Stories-Lottery-End

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

    // Stories-Lottery-Start
    private XenoEvolveBuiState BuildEvolveState(Entity<XenoEvolutionComponent> xeno)
    {
        var lotteryChoices = new List<EntProtoId>();

        if (_tierLotteryEnabled)
        {
            foreach (var choice in xeno.Comp.EvolvesTo)
            {
                if (IsLotteryOpenFor(xeno, choice, out _))
                    lotteryChoices.Add(choice);
            }
        }

        return new XenoEvolveBuiState(LackingOvipositor(), lotteryChoices);
    }

    /// <summary>
    /// Whether this xeno can currently register for the tier lottery towards <paramref name="choice"/>:
    /// the choice is a lottery caste and its tier's draw is still pending and in the future.
    /// </summary>
    private bool IsLotteryOpenFor(Entity<XenoEvolutionComponent> xeno, EntProtoId choice, out int tier)
    {
        tier = 0;

        if (!_tierLotteryEnabled || _xenoHive.GetHive(xeno.Owner) is not { } hive)
            return false;

        if (!_xenoHive.IsLotteryCaste(hive, choice) || !TryGetCasteTier(choice, out tier))
            return false;

        return _xenoHive.IsLotteryPending(hive, tier, out var at) && _gameTicker.RoundDuration() < at;
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

    private void UpdateLotteries(TimeSpan roundDuration)
    {
        var hives = EntityQueryEnumerator<HiveComponent>();
        while (hives.MoveNext(out var hiveId, out var hive))
        {
            var drew = false;
            while (_xenoHive.TryGetDueLotteryTier((hiveId, hive), roundDuration, out var tier))
            {
                // Mark the tier drawn before running so CanEvolvePopup (used per winner) no longer treats
                // the lottery as pending and blocks the evolutions we're handing out.
                _xenoHive.MarkLotteryDrawn((hiveId, hive), tier);
                RunLottery((hiveId, hive), tier);
                drew = true;
            }

            if (drew)
                RefreshHiveEvolutionUis(hiveId);
        }
    }

    /// <summary>
    /// Draws the one-time evolution lottery for a tier: the registrants are shuffled and each is offered
    /// the evolution through the normal <see cref="CanEvolvePopup"/> checks, so the live tier cap limits
    /// how many win. Everyone who can't evolve is told they were not chosen.
    /// </summary>
    private void RunLottery(Entity<HiveComponent> hive, int tier)
    {
        var registrants = new List<(Entity<XenoEvolutionComponent> Xeno, EntProtoId Target)>();

        var query = EntityQueryEnumerator<XenoLotteryRegistrationComponent, XenoEvolutionComponent, HiveMemberComponent>();
        while (query.MoveNext(out var uid, out var registration, out var evo, out var member))
        {
            if (member.Hive != hive.Owner)
                continue;

            if (!TryGetCasteTier(registration.Target, out var targetTier) || targetTier != tier)
                continue;

            registrants.Add(((uid, evo), registration.Target));
        }

        if (registrants.Count == 0)
            return;

        // Hand out slots by running each shuffled registrant through the normal evolution checks instead
        // of a precomputed slot count. As winners evolve the live tier cap fills, so the rest lose; this
        // also enforces the target's own unlock time, caste caps and same-caste cooldown.
        _random.Shuffle(registrants);

        foreach (var (registrant, target) in registrants)
        {
            // Re-check damage: the wait after registering is long enough to get hit. Dead fall through below.
            if (!_mobState.IsDead(registrant) && !DamagedCheckPopup(registrant, predicted: false, doPopup: false))
            {
                RemComp<XenoLotteryRegistrationComponent>(registrant);
                _popup.PopupEntity(Loc.GetString("rmc-xeno-lottery-damaged"), registrant, registrant, PopupType.LargeCaution);
                continue;
            }

            if (!CanWinLottery(registrant, target))
            {
                RemComp<XenoLotteryRegistrationComponent>(registrant);
                _popup.PopupEntity(Loc.GetString("rmc-xeno-lottery-lost"), registrant, registrant, PopupType.LargeCaution);
                continue;
            }

            // The old entity (and its registration) is deleted by EvolveXeno, so capture the log name first.
            var prettyOld = ToPrettyString(registrant);
            var newXeno = EvolveXeno(registrant, target, subtractPoints: true, endPopup: false);
            _popup.PopupEntity(Loc.GetString("rmc-xeno-lottery-won"), newXeno, newXeno, PopupType.Large);
            _adminLog.Add(LogType.RMCEvolve, $"Xenonid {prettyOld} won the tier {tier} evolution lottery into {target.Id}");
        }
    }

    /// <summary>
    /// Whether a lottery registrant may currently win: alive, player-controlled, can afford the evolution
    /// and passes the normal evolution checks for the target caste. Damage is gated separately by the caller.
    /// </summary>
    private bool CanWinLottery(Entity<XenoEvolutionComponent> xeno, EntProtoId target)
    {
        if (_mobState.IsDead(xeno))
            return false;

        if (!_mind.TryGetMind(xeno, out _, out _))
            return false;

        if (xeno.Comp.Points < xeno.Comp.Max)
            return false;

        return CanEvolvePopup(xeno, target, doPopup: false);
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
    // Stories-Lottery-End
}
