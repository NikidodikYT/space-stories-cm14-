using Content.Shared._RMC14.Xenonids;
using Content.Shared.Mobs.Components;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Single source of truth for "is this entity a valid Despoiler ability
/// victim?" — drops self-hits, anything without <see cref="MobStateComponent"/>,
/// and friendly xenos. Used by every AoE/lookup loop in the Despoiler
/// kit so the filter can never drift between abilities.
/// </summary>
public static class XenoDespoilerVictims
{
    public static bool IsValidVictim(IEntityManager entities, EntityUid victim, EntityUid caster)
    {
        if (victim == caster)
            return false;
        if (!entities.HasComponent<MobStateComponent>(victim))
            return false;
        if (entities.HasComponent<XenoComponent>(victim))
            return false;
        return true;
    }
}
