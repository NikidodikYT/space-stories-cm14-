using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Lingering Acid puddle behavior. Decay window jittered 15-20s on spawn.
/// On Cross by a non-xeno that isn't currently being pulled: 20 BURN + brief
/// slowdown placeholder (slowdown component application TODO — hook into
/// existing RMC14 acid slowdown).
/// </summary>
public sealed class XenoDespoilerLingeringAcidSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerLingeringAcidComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<XenoDespoilerLingeringAcidComponent, StartCollideEvent>(OnCollide);
    }

    private void OnInit(EntityUid uid, XenoDespoilerLingeringAcidComponent comp, ComponentInit args)
    {
        var jitter = _random.NextFloat(comp.MinLifetimeSeconds, comp.MaxLifetimeSeconds);
        comp.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(jitter);
        Dirty(uid, comp);
    }

    private void OnCollide(EntityUid uid, XenoDespoilerLingeringAcidComponent comp, ref StartCollideEvent args)
    {
        var target = args.OtherEntity;
        if (!HasComp<MobStateComponent>(target))
            return;

        if (HasComp<XenoComponent>(target))
            return;

        // TRAIT_HAULED proxy: if currently being pulled, skip.
        if (TryComp<PullableComponent>(target, out var pull) && pull.BeingPulled)
            return;

        var dmg = new DamageSpecifier();
        dmg.DamageDict["Heat"] = FixedPoint2.New(comp.CrossBurnDamage);
        _damageable.TryChangeDamage(target, dmg, ignoreResistances: false, origin: comp.Owner);

        // TODO: hook the canonical RMC14 xeno acid-slowdown component here
        // (Content.Shared._RMC14.Xenonids.Acid.* slow component) — kept as a
        // single integration point so we don't duplicate the slow effect.
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoDespoilerLingeringAcidComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.ExpiresAt)
                QueueDel(uid);
        }
    }
}
