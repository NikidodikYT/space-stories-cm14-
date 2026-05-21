using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared._RMC14.Xenonids.Stab;
using Content.Shared._RMC14.Xenonids.Projectile.Spit.Charge;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerFinishingStabSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly XenoDespoilerAcidSystem _acid = default!;

    private EntityQuery<UserAcidedComponent> _acidQuery;

    public override void Initialize()
    {
        _acidQuery = GetEntityQuery<UserAcidedComponent>();

        SubscribeLocalEvent<XenoDespoilerComponent, RMCGetTailStabBonusDamageEvent>(OnGetTailStabBonus);
    }

    private void OnGetTailStabBonus(EntityUid uid, XenoDespoilerComponent comp, ref RMCGetTailStabBonusDamageEvent args)
    {
        if (args.Target is not { } target)
            return;

        if (!_acidQuery.HasComp(target))
            return;

        var tier = _acid.ConsumeAcidTier(target);
        if (tier <= 0)
            return;

        var table = comp.FinishingStabBonusByTier;
        if (table.Count == 0)
            return;

        var idx = Math.Clamp(tier - 1, 0, table.Count - 1);
        var bonus = table[idx];
        if (bonus <= 0)
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict["Heat"] = FixedPoint2.New(bonus);
        _damageable.TryChangeDamage(target, damage, ignoreResistances: true, origin: uid);
    }
}
