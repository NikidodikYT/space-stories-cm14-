using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared._RMC14.Xenonids.Stab;
using Content.Shared._RMC14.Xenonids.Projectile.Spit.Charge;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerFinishingStabSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly XenoDespoilerAcidSystem _acid = default!;

    private EntityQuery<UserAcidedComponent> _acidQuery;

    public override void Initialize()
    {
        _acidQuery = GetEntityQuery<UserAcidedComponent>();

        SubscribeLocalEvent<XenoDespoilerComponent, RMCGetTailStabBonusDamageEvent>(OnGetTailStabBonus);
        SubscribeLocalEvent<XenoDespoilerComponent, MeleeHitEvent>(OnMeleeHit);
    }

    // CurTime is the only signal that the upcoming MeleeHitEvent comes from a tail stab.
    private void OnGetTailStabBonus(EntityUid uid, XenoDespoilerComponent comp, ref RMCGetTailStabBonusDamageEvent args)
    {
        var server = EnsureComp<XenoDespoilerServerComponent>(uid);
        server.LastTailStabTime = _timing.CurTime;
    }

    private void OnMeleeHit(EntityUid uid, XenoDespoilerComponent comp, MeleeHitEvent args)
    {
        if (!TryComp<XenoDespoilerServerComponent>(uid, out var server) ||
            server.LastTailStabTime != _timing.CurTime)
            return;

        server.LastTailStabTime = null;

        var table = comp.FinishingStabBonusByTier;
        if (table.Count == 0)
            return;

        foreach (var target in args.HitEntities)
        {
            if (!_acidQuery.HasComp(target))
                continue;

            var tier = _acid.ConsumeAcidTier(target);
            if (tier <= 0)
                continue;

            var idx = Math.Clamp(tier - 1, 0, table.Count - 1);
            var bonus = table[idx];
            if (bonus <= 0)
                continue;

            var damage = new DamageSpecifier();
            damage.DamageDict["Heat"] = FixedPoint2.New(bonus);
            _damageable.TryChangeDamage(target, damage, ignoreResistances: true, origin: uid);
        }
    }
}
