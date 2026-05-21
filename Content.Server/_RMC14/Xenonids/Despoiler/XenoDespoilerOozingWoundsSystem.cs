using System.Numerics;
using Content.Shared._RMC14.Actions;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Oozing Wounds.
///
///   Severity = (HP &lt;= 70%) + (HP &lt;= 30%), radius = BaseRadius + severity
///   (so 1 / 2 / 3 tiles at 100% / wounded / critical HP).
///
///   On cast we spawn the yellow telegraph IMMEDIATELY on every ring tile and
///   enqueue the actual spray spawn into a per-caster
///   <see cref="XenoDespoilerOozingWoundsPendingComponent"/>. Each spawn is delayed by
///   <c>DistanceDelayPerTileSeconds × Chebyshev(tile, caster)</c> so the wave
///   expands outward (CM13 <c>addtimer(..., 0.2 SECONDS * get_dist())</c>).
///   The per-caster queue means two despoilers casting at once don't bleed
///   their waves into a single global list.
///
///   20% chance per tile to drop a Lingering Acid puddle on top of the spray
///   (DM <c>prob(20)</c>).
/// </summary>
public sealed class XenoDespoilerOozingWoundsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedRMCActionsSystem _rmcActions = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly XenoDespoilerCatalyzeFlagSystem _catalyze = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerComponent, XenoDespoilerOozingWoundsActionEvent>(OnUse);
    }

    private void OnUse(EntityUid uid, XenoDespoilerComponent comp, XenoDespoilerOozingWoundsActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<XenoDespoilerOozingWoundsActionComponent>(args.Action, out var action))
            return;

        if (!_rmcActions.TryUseAction(args))
            return;

        var empowered = _catalyze.TakeEmpowerment(uid, comp);

        var severity = ComputeSeverity(uid, action);
        var radius = action.BaseRadius + severity;
        var origin = Transform(uid).Coordinates;
        var sprayProto = empowered ? action.AcidSprayEmpoweredProto : action.AcidSprayProto;
        var now = _timing.CurTime;
        var perTile = action.DistanceDelayPerTileSeconds;

        var pending = EnsureComp<XenoDespoilerOozingWoundsPendingComponent>(uid);

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var euclid = MathF.Sqrt(dx * dx + dy * dy);
                if (euclid > radius + 0.001f)
                    continue;

                // Chebyshev for tile-counted delay (CM13 get_dist semantics);
                // euclidean is only used to clip the ring to a circle.
                var cheby = Math.Max(Math.Abs(dx), Math.Abs(dy));
                var tile = origin.Offset(new Vector2(dx, dy)).SnapToGrid(EntityManager);

                var telegraph = Spawn(action.TelegraphProto, tile);
                _hive.SetSameHive(uid, telegraph);

                pending.Pending.Add(new XenoDespoilerOozingWoundsPendingTile
                {
                    SpawnAt = now + TimeSpan.FromSeconds(perTile * cheby),
                    Tile = tile,
                    SprayProto = sprayProto,
                    PuddleProto = action.LingeringAcidProto,
                    Empowered = empowered,
                    StunSeconds = action.EmpoweredStunSeconds,
                    ImmunitySeconds = action.EmpoweredImmunitySeconds,
                    PuddleChance = action.LingeringAcidChance,
                });
            }
        }

        if (action.CastSound is { } sound)
            _audio.PlayPvs(sound, uid);

        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoDespoilerOozingWoundsPendingComponent>();
        while (query.MoveNext(out var caster, out var pending))
        {
            // Walk in reverse so we can drop in place. Anything still
            // scheduled in the future stays; matured entries fire and exit.
            for (var i = pending.Pending.Count - 1; i >= 0; i--)
            {
                var entry = pending.Pending[i];
                if (now < entry.SpawnAt)
                    continue;

                pending.Pending.RemoveAt(i);

                if (TerminatingOrDeleted(caster))
                    continue;

                var spray = Spawn(entry.SprayProto, entry.Tile);
                _hive.SetSameHive(caster, spray);

                if (TryComp<XenoDespoilerAcidSprayComponent>(spray, out var sprayComp))
                {
                    sprayComp.Owner = caster;
                    sprayComp.StunsOnEmpowered = entry.Empowered;
                    sprayComp.StunSeconds = entry.StunSeconds;
                    sprayComp.GrantImmunitySeconds = entry.ImmunitySeconds;
                    Dirty(spray, sprayComp);
                }

                if (_random.Prob(entry.PuddleChance))
                {
                    var puddle = Spawn(entry.PuddleProto, entry.Tile);
                    _hive.SetSameHive(caster, puddle);
                    if (TryComp<XenoDespoilerLingeringAcidComponent>(puddle, out var puddleComp))
                    {
                        puddleComp.Owner = caster;
                        Dirty(puddle, puddleComp);
                    }
                }
            }

            if (pending.Pending.Count == 0)
                RemCompDeferred<XenoDespoilerOozingWoundsPendingComponent>(caster);
        }
    }

    private int ComputeSeverity(EntityUid uid, XenoDespoilerOozingWoundsActionComponent action)
    {
        if (!TryComp<DamageableComponent>(uid, out var dmg))
            return 0;
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return 0;

        float deadThreshold = 0;
        foreach (var t in thresholds.Thresholds)
        {
            if (t.Value == MobState.Dead && (float) t.Key > deadThreshold)
                deadThreshold = (float) t.Key;
        }
        if (deadThreshold <= 0)
            return 0;

        var damageTotal = (float) dmg.TotalDamage;
        var hpFrac = 1f - Math.Clamp(damageTotal / deadThreshold, 0f, 1f);

        var severity = 0;
        if (hpFrac <= action.SeverityHpThreshold1) severity++;
        if (hpFrac <= action.SeverityHpThreshold2) severity++;
        return severity;
    }
}
