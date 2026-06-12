using Content.Shared._RMC14.Map;
using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;

namespace Content.Server._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerAcidBarrageProjectileSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly RMCMapSystem _rmcMap = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly XenoDespoilerAcidSystem _acid = default!;

    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<XenoComponent> _xenoQuery;
    private EntityQuery<XenoDespoilerLingeringAcidComponent> _lingeringQuery;

    public override void Initialize()
    {
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _xenoQuery = GetEntityQuery<XenoComponent>();
        _lingeringQuery = GetEntityQuery<XenoDespoilerLingeringAcidComponent>();

        SubscribeLocalEvent<XenoDespoilerAcidBarrageProjectileComponent, ProjectileHitEvent>(OnHit);
        SubscribeLocalEvent<XenoDespoilerAcidBarrageProjectileComponent, EntityTerminatingEvent>(OnTerminate);
    }

    private void OnHit(EntityUid uid, XenoDespoilerAcidBarrageProjectileComponent comp, ref ProjectileHitEvent args)
    {
        if (args.Handled || comp.Shooter is not { } shooter)
            return;

        if (_mobStateQuery.HasComp(args.Target) && !_xenoQuery.HasComp(args.Target))
            _acid.ApplyAcid(args.Target, shooter);

        foreach (var marine in _lookup.GetEntitiesInRange<MarineComponent>(_transform.GetMapCoordinates(args.Target), 1f))
        {
            if (marine.Owner != args.Target)
                _acid.ApplyAcid(marine, shooter);
        }
    }

    private void OnTerminate(EntityUid uid, XenoDespoilerAcidBarrageProjectileComponent comp, ref EntityTerminatingEvent args)
    {
        if (!_xformQuery.TryComp(uid, out var xform))
            return;

        if (TerminatingOrDeleted(xform.ParentUid))
            return;

        var coords = xform.Coordinates.SnapToGrid(EntityManager);
        using var anchored = _rmcMap.GetAnchoredEntitiesEnumerator<XenoDespoilerLingeringAcidComponent>(coords);
        if (anchored.MoveNext(out _))
            return;

        var puddle = Spawn(comp.LingeringAcidProto, coords);
        if (comp.Shooter is { } shooter)
            _hive.SetSameHive(shooter, puddle);

        if (_lingeringQuery.TryComp(puddle, out var puddleComp))
        {
            puddleComp.Caster = comp.Shooter;
            Dirty(puddle, puddleComp);
        }
    }
}
