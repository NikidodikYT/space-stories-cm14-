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

public sealed class XenoDespoilerLingeringAcidSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<XenoComponent> _xenoQuery;
    private EntityQuery<PullableComponent> _pullableQuery;

    public override void Initialize()
    {
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _xenoQuery = GetEntityQuery<XenoComponent>();
        _pullableQuery = GetEntityQuery<PullableComponent>();

        SubscribeLocalEvent<XenoDespoilerLingeringAcidComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<XenoDespoilerLingeringAcidComponent, StartCollideEvent>(OnCollide);
    }

    private void OnInit(EntityUid uid, XenoDespoilerLingeringAcidComponent comp, ComponentInit args)
    {
        var min = comp.MinLifetime.TotalSeconds;
        var max = comp.MaxLifetime.TotalSeconds;
        var jitter = TimeSpan.FromSeconds(_random.NextFloat((float)min, (float)max));
        comp.ExpiresAt = _timing.CurTime + jitter;
        Dirty(uid, comp);
    }

    private void OnCollide(EntityUid uid, XenoDespoilerLingeringAcidComponent comp, ref StartCollideEvent args)
    {
        var target = args.OtherEntity;
        if (!_mobStateQuery.HasComp(target) || _xenoQuery.HasComp(target))
            return;

        if (_pullableQuery.TryComp(target, out var pull) && pull.BeingPulled)
            return;

        var dmg = new DamageSpecifier();
        dmg.DamageDict["Heat"] = FixedPoint2.New(comp.CrossBurnDamage);
        _damageable.TryChangeDamage(target, dmg, ignoreResistances: false, origin: comp.Caster);
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
