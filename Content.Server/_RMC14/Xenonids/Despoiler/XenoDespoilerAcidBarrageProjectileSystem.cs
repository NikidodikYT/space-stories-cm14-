using System.Numerics;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Acid Barrage projectile lifecycle.
///
///   On hit (ProjectileHitEvent):
///     Apply acid (+ enhance) to a marine target and splash acid to
///     <see cref="XenoDespoilerAcidBarrageProjectileComponent.SplashRadius"/>-
///     tile neighbours. Direct-hit damage is handled by the base ProjectileComponent
///     on the prototype.
///
///   On any despawn (hit-and-deleted, max-range timeout, lifetime expiry):
///     Roll <c>LingeringAcidChance</c> and drop an acid puddle at the
///     projectile's last position. Single source of truth for puddle spawns —
///     the hit handler no longer rolls its own roll, since the deletion that
///     follows ProjectileHitEvent immediately fires this handler.
/// </summary>
public sealed class XenoDespoilerAcidBarrageProjectileSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly XenoDespoilerAcidSystem _acid = default!;

    /// <summary>
    /// Default acid duration applied to splash victims. Kept as a constant
    /// because the projectile prototype doesn't expose a per-shot field for
    /// it — Acid Barrage shares this with melee slashes.
    /// </summary>
    private const float SplashAcidDurationSeconds = 12f;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerAcidBarrageProjectileComponent, ProjectileHitEvent>(OnHit);
        SubscribeLocalEvent<XenoDespoilerAcidBarrageProjectileComponent, EntityTerminatingEvent>(OnTerminate);
    }

    private void OnHit(EntityUid uid, XenoDespoilerAcidBarrageProjectileComponent comp, ref ProjectileHitEvent args)
    {
        var coords = Transform(uid).Coordinates;

        if (comp.Shooter is { } shooter)
        {
            if (HasComp<MobStateComponent>(args.Target) && XenoDespoilerVictims.IsValidVictim(EntityManager, args.Target, shooter))
            {
                _acid.ApplyAcid(args.Target, shooter, SplashAcidDurationSeconds);
                if (comp.EnhanceAcid)
                    _acid.EnhanceAcid(args.Target);
            }
        }

        var mapCoords = _xform.ToMapCoordinates(coords);
        var halfExtent = comp.SplashRadius + 0.5f;
        var box = Box2.CenteredAround(mapCoords.Position, new Vector2(halfExtent * 2f, halfExtent * 2f));

        foreach (var ent in _lookup.GetEntitiesIntersecting(mapCoords.MapId, box))
        {
            if (ent == args.Target || ent == uid)
                continue;
            if (comp.Shooter is not { } src || !XenoDespoilerVictims.IsValidVictim(EntityManager, ent, src))
                continue;
            _acid.ApplyAcid(ent, comp.Shooter, SplashAcidDurationSeconds);
        }
    }

    private void OnTerminate(EntityUid uid, XenoDespoilerAcidBarrageProjectileComponent comp, ref EntityTerminatingEvent args)
    {
        if (!TryComp<TransformComponent>(uid, out var xform))
            return;

        // Parent already dying (map shutdown, grid removed) — don't orphan
        // the puddle on a dead grid.
        if (TerminatingOrDeleted(xform.ParentUid))
            return;

        if (!_random.Prob(comp.LingeringAcidChance))
            return;

        var puddle = Spawn("RMCEffectDespoilerLingeringAcid", xform.Coordinates.SnapToGrid(EntityManager));
        if (comp.Shooter is { } shooter)
            _hive.SetSameHive(shooter, puddle);

        if (TryComp<XenoDespoilerLingeringAcidComponent>(puddle, out var puddleComp))
        {
            puddleComp.Owner = comp.Shooter;
            Dirty(puddle, puddleComp);
        }
    }
}
