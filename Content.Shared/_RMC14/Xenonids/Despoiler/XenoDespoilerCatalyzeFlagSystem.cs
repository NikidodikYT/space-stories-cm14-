using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Read-only helpers for the Catalyze "next ability empowered" flag.
///
/// The flag is *set* on the server (Catalyze action handler) and consumed
/// on the server (each ability handler). This shared helper exists so
/// systems on either side can query it without recursing into the server.
/// </summary>
public sealed class XenoDespoilerCatalyzeFlagSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public bool IsEmpowered(EntityUid uid, XenoDespoilerComponent comp)
    {
        if (!comp.NextAbilityEmpowered)
            return false;
        return _timing.CurTime <= comp.EmpowerExpiresAt;
    }

    /// <summary>
    /// Consume the empowerment flag (returns true if it was active).
    /// </summary>
    public bool TakeEmpowerment(EntityUid uid, XenoDespoilerComponent comp)
    {
        if (!IsEmpowered(uid, comp))
        {
            if (comp.NextAbilityEmpowered)
            {
                comp.NextAbilityEmpowered = false;
                Dirty(uid, comp);
            }
            return false;
        }

        comp.NextAbilityEmpowered = false;
        Dirty(uid, comp);
        return true;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoDespoilerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextAbilityEmpowered && now > comp.EmpowerExpiresAt)
            {
                comp.NextAbilityEmpowered = false;
                Dirty(uid, comp);
            }
        }
    }
}
