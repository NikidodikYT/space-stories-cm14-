using System.Numerics;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Random;

namespace Content.Server._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerAcidBarrageProjectileSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly XenoDespoilerAcidSystem _acid = default!;

    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<XenoDespoilerLingeringAcidComponent> _lingeringQuery;

    public override void Initialize()
    {
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _lingeringQuery = GetEntityQuery<XenoDespoilerLingeringAcidComponent>();

        SubscribeLocalEvent<XenoDespoilerAcidBarrageProjectileComponent, ProjectileHitEvent>(OnHit);
        SubscribeLocalEvent<XenoDespoilerAcidBarrageProjectileComponent, EntityTerminatingEvent>(OnTerminate);
    }

    private void OnHit(EntityUid uid, XenoDespoilerAcidBarrageProjectileComponent comp, ref ProjectileHitEvent args)
    {
        if (!_xformQuery.TryComp(uid, out var xform))
            return;

        var shooter = comp.Shooter;
        if (shooter is { } src)
        {
            if (_mobStateQuery.HasComp(args.Target) && XenoDespoilerVictims.IsValidVictim(EntityManager, args.Target, src))
            {
                _acid.ApplyAcid(args.Target, src, enhance: comp.EnhanceAcid);
            }
        }

        var mapCoords = _xform.ToMapCoordinates(xform.Coordinates);
        var halfExtent = comp.SplashRadius + 0.5f;
        var box = Box2.CenteredAround(mapCoords.Position, new Vector2(halfExtent * 2f, halfExtent * 2f));

        foreach (var ent in _lookup.GetEntitiesIntersecting(mapCoords.MapId, box))
        {
            if (ent == args.Target || ent == uid)
                continue;
            if (shooter is not { } caster || !XenoDespoilerVictims.IsValidVictim(EntityManager, ent, caster))
                continue;
            _acid.ApplyAcid(ent, caster);
        }
    }

    private void OnTerminate(EntityUid uid, XenoDespoilerAcidBarrageProjectileComponent comp, ref EntityTerminatingEvent args)
    {
        if (!_xformQuery.TryComp(uid, out var xform))
            return;

        if (TerminatingOrDeleted(xform.ParentUid))
            return;

        if (!_random.Prob(comp.LingeringAcidChance))
            return;

        var puddle = Spawn(comp.LingeringAcidProto, xform.Coordinates.SnapToGrid(EntityManager));
        if (comp.Shooter is { } shooter)
            _hive.SetSameHive(shooter, puddle);

        if (_lingeringQuery.TryComp(puddle, out var puddleComp))
        {
            puddleComp.Caster = comp.Shooter;
            Dirty(puddle, puddleComp);
        }
    }
}
