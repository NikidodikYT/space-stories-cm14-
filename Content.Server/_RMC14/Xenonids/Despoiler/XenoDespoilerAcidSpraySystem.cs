using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Acid Spray contact handler. When a non-xeno mob walks onto an active spray:
///   * Damage + acid level up.
///   * Empowered variant also stuns and grants 3-second acid immunity.
///
/// Also cleans up XenoDespoilerAcidImmunityComponent on expiry.
/// </summary>
public sealed class XenoDespoilerAcidSpraySystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly XenoDespoilerAcidSystem _acid = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerAcidSprayComponent, StartCollideEvent>(OnCollide);
    }

    private void OnCollide(EntityUid uid, XenoDespoilerAcidSprayComponent comp, ref StartCollideEvent args)
    {
        var target = args.OtherEntity;
        if (!HasComp<MobStateComponent>(target))
            return;

        if (HasComp<XenoComponent>(target))
            return;

        if (HasComp<XenoDespoilerAcidImmunityComponent>(target))
            return;

        var dmg = new DamageSpecifier();
        dmg.DamageDict["Heat"] = FixedPoint2.New(comp.Damage);
        _damageable.TryChangeDamage(target, dmg, ignoreResistances: false, origin: comp.Owner);

        _acid.ApplyAcid(target, comp.Owner, 10f);
        _acid.EnhanceAcid(target);

        if (comp.StunsOnEmpowered)
        {
            _stun.TryParalyze(target, TimeSpan.FromSeconds(comp.StunSeconds), true);

            var immunity = EnsureComp<XenoDespoilerAcidImmunityComponent>(target);
            var expires = _timing.CurTime + TimeSpan.FromSeconds(comp.GrantImmunitySeconds);
            if (expires > immunity.ExpiresAt)
                immunity.ExpiresAt = expires;
            Dirty(target, immunity);
        }
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoDespoilerAcidImmunityComponent>();
        while (query.MoveNext(out var uid, out var immunity))
        {
            if (now >= immunity.ExpiresAt)
                RemComp<XenoDespoilerAcidImmunityComponent>(uid);
        }
    }
}
