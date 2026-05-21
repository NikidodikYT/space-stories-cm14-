using Content.Shared._RMC14.Actions;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Catalyze: spends one Hypertension stack and arms the next Despoiler ability
/// for a brief empower window.
///
/// Also owns the lifecycle of the world-space "Hypertension burst" visual.
/// The visual is despawned the instant the empower flag goes away — either
/// consumed by one of the 4 Despoiler abilities, or its window expired — so
/// we don't wait for the entity's own TimedDespawn.
///
/// ComponentInit hook wipes a stale <see cref="XenoDespoilerComponent.CatalyzeVisual"/>
/// reference. <c>EntityUid?</c> deserialises across save/load and might point
/// at a dead entity from a previous round; clearing it on init avoids the
/// "first Catalyze of the round leaks because we tried to delete a ghost UID"
/// edge case.
/// </summary>
public sealed class XenoDespoilerCatalyzeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedRMCActionsSystem _rmcActions = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly XenoDespoilerHypertensionSystem _hyper = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerComponent, XenoDespoilerCatalyzeActionEvent>(OnCatalyze);
        SubscribeLocalEvent<XenoDespoilerComponent, ComponentInit>(OnDespoilerInit);
        SubscribeLocalEvent<XenoDespoilerComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnDespoilerInit(EntityUid uid, XenoDespoilerComponent comp, ComponentInit args)
    {
        // Wipe any stale visual reference from prior round / pre-save data.
        comp.CatalyzeVisual = null;
    }

    private void OnCatalyze(EntityUid uid, XenoDespoilerComponent comp, XenoDespoilerCatalyzeActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<XenoDespoilerHypertensionComponent>(uid, out var hyper))
            return;

        if (!TryComp<XenoDespoilerCatalyzeActionComponent>(args.Action, out var action))
            return;

        if (!_hyper.TrySpendStacks(uid, hyper, action.HypertensionCost))
        {
            _popup.PopupEntity(Loc.GetString("rmc-despoiler-no-hypertension"), uid, uid);
            return;
        }

        if (!_rmcActions.TryUseAction(args))
            return;

        comp.NextAbilityEmpowered = true;
        comp.EmpowerExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(action.BuffDurationSeconds);

        // If a previous Catalyze visual is still alive (overlapping recast),
        // kill it before spawning the new one so we never leak entities.
        DespawnVisual(comp);

        var burst = Spawn(action.VisualProto, Transform(uid).Coordinates);
        _xform.SetParent(burst, uid);
        _hive.SetSameHive(uid, burst);
        comp.CatalyzeVisual = burst;
        Dirty(uid, comp);

        _popup.PopupEntity(Loc.GetString("rmc-despoiler-catalyze-active"), uid, uid);
        args.Handled = true;
    }

    private void OnShutdown(EntityUid uid, XenoDespoilerComponent comp, ComponentShutdown args)
    {
        DespawnVisual(comp);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<XenoDespoilerComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.NextAbilityEmpowered)
                continue;

            if (comp.CatalyzeVisual is not null)
                DespawnVisual(comp);
        }
    }

    private void DespawnVisual(XenoDespoilerComponent comp)
    {
        if (comp.CatalyzeVisual is not { } visual)
            return;

        if (Exists(visual) && !TerminatingOrDeleted(visual))
            QueueDel(visual);

        comp.CatalyzeVisual = null;
    }
}
