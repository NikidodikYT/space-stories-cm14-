using Content.Shared._RMC14.Xenonids.Stab;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Implements the Despoiler's Tail Stab passive damage modifier.
/// When the Despoiler lands a standard tail stab, we check for active acid tiers
/// on the target and apply bonus heat damage ignoring resistances.
/// </summary>
public sealed class XenoDespoilerFinishingStabSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerComponent, RMCGetTailStabBonusDamageEvent>(OnGetTailStabBonusDamage);
        SubscribeLocalEvent<XenoDespoilerComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnGetTailStabBonusDamage(EntityUid uid, XenoDespoilerComponent comp, ref RMCGetTailStabBonusDamageEvent args)
    {
        comp.LastTailStabTime = _timing.CurTime;
    }

    private void OnMeleeHit(EntityUid uid, XenoDespoilerComponent comp, MeleeHitEvent args)
    {
        if (comp.LastTailStabTime != _timing.CurTime)
            return;

        comp.LastTailStabTime = null;

        foreach (var target in args.HitEntities)
        {
            if (TerminatingOrDeleted(target))
                continue;

            if (TryComp<XenoDespoilerAcidEffectComponent>(target, out var acid))
            {
                var level = acid.Level;
                if (level > 0)
                {
                    var bonusDamage = 15 * Math.Clamp(level, 0, 3);
                    var damage = new DamageSpecifier();
                    damage.DamageDict["Heat"] = bonusDamage;

                    _damageable.TryChangeDamage(target, damage, ignoreResistances: true, origin: uid);

                    // Decrement target's acid stack by 1 on hit
                    acid.Level--;
                    if (acid.Level <= 0)
                    {
                        RemComp<XenoDespoilerAcidEffectComponent>(target);
                    }
                    else
                    {
                        Dirty(target, acid);
                    }
                }
            }
        }
    }
}
