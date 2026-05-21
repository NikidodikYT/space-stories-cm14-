using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Stab;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Server-side: every Despoiler slash applies / extends the Acid effect on
/// the target, optionally enhances it (at hypertension stacks >= threshold),
/// and adds bonus burn damage (+5 per stack) via GetMeleeDamageEvent.
/// The Acid effect itself no longer deals damage over time — it only tracks
/// level for armor debuffs and expires after its duration.
/// </summary>
public sealed class XenoDespoilerAcidSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly XenoDespoilerHypertensionSystem _hyper = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerSlashOnHitComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<XenoDespoilerSlashOnHitComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<XenoDespoilerSlashOnHitComponent, RMCGetTailStabBonusDamageEvent>(OnGetTailStabBonusDamage);
    }

    private void OnGetMeleeDamage(EntityUid uid, XenoDespoilerSlashOnHitComponent comp, ref GetMeleeDamageEvent args)
    {
        // Despoiler bonus burn = stacks * BonusBurnPerStack.
        if (!TryComp<XenoDespoilerHypertensionComponent>(uid, out var hyper) || hyper.Stacks <= 0)
            return;

        var bonus = hyper.Stacks * hyper.BonusBurnPerStack;
        if (bonus <= 0)
            return;

        var burn = new DamageSpecifier();
        burn.DamageDict["Heat"] = FixedPoint2.New(bonus);
        args.Damage = args.Damage + burn;
    }

    // Tail stab raises its own bonus-damage event instead of GetMeleeDamageEvent,
    // so the hypertension burn bonus would otherwise be skipped at every stack.
    private void OnGetTailStabBonusDamage(EntityUid uid, XenoDespoilerSlashOnHitComponent comp, ref RMCGetTailStabBonusDamageEvent args)
    {
        if (!TryComp<XenoDespoilerHypertensionComponent>(uid, out var hyper) || hyper.Stacks <= 0)
            return;

        var bonus = hyper.Stacks * hyper.BonusBurnPerStack;
        if (bonus <= 0)
            return;

        var burn = new DamageSpecifier();
        burn.DamageDict["Heat"] = FixedPoint2.New(bonus);
        args.Damage += burn;
    }

    private void OnMeleeHit(EntityUid uid, XenoDespoilerSlashOnHitComponent comp, MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        // Despoiler must be a real xeno. Otherwise we don't run.
        if (!HasComp<XenoComponent>(uid))
            return;

        TryComp<XenoDespoilerHypertensionComponent>(uid, out var hyper);

        foreach (var hit in args.HitEntities)
        {
            if (hit == uid)
                continue;

            // Skip allies (other xenos).
            if (HasComp<XenoComponent>(hit))
                continue;

            ApplyAcid(hit, uid, comp.AcidApplyDuration);

            if (hyper != null && hyper.Stacks >= comp.EnhanceStacksThreshold)
                EnhanceAcid(hit);

            // Hypertension only builds on marine hits — slashes against
            // synthetics, xeno-friendly NPCs, structures, etc. don't count.
            if (hyper != null && HasComp<MarineComponent>(hit))
                _hyper.AddSlashPoints(uid, hyper);
        }
    }

    public XenoDespoilerAcidEffectComponent ApplyAcid(EntityUid target, EntityUid? source, float durationSeconds)
    {
        var effect = EnsureComp<XenoDespoilerAcidEffectComponent>(target);
        if (effect.Level < 1)
            effect.Level = 1;

        var now = _timing.CurTime;
        var newExpiry = now + TimeSpan.FromSeconds(durationSeconds);
        if (newExpiry > effect.ExpiresAt)
            effect.ExpiresAt = newExpiry;

        if (effect.NextTickAt == TimeSpan.Zero)
            effect.NextTickAt = now + TimeSpan.FromSeconds(effect.TickIntervalSeconds);

        Dirty(target, effect);
        return effect;
    }

    public void EnhanceAcid(EntityUid target)
    {
        if (!TryComp<XenoDespoilerAcidEffectComponent>(target, out var effect))
        {
            var defaults = new XenoDespoilerAcidEffectComponent();
            effect = ApplyAcid(target, null, defaults.DurationSeconds);
        }

        if (effect.Level < effect.MaxLevel)
        {
            effect.Level++;
            Dirty(target, effect);
        }

        effect.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(effect.DurationSeconds);
        Dirty(target, effect);
    }

    public void SuperEnhanceAcid(EntityUid target)
    {
        var effect = EnsureComp<XenoDespoilerAcidEffectComponent>(target);
        effect.Level = effect.MaxLevel;
        effect.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(effect.DurationSeconds);
        if (effect.NextTickAt == TimeSpan.Zero)
            effect.NextTickAt = _timing.CurTime + TimeSpan.FromSeconds(effect.TickIntervalSeconds);
        Dirty(target, effect);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoDespoilerAcidEffectComponent>();
        while (query.MoveNext(out var uid, out var effect))
        {
            // Acid effect is now a level/duration tracker only — no DoT.
            // All damage is dealt up front by the hit that applied it.
            if (now >= effect.ExpiresAt)
                RemComp<XenoDespoilerAcidEffectComponent>(uid);
        }
    }
}
