using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Pure resource tracker — no spawns, no status effects, no DamageSpecifier mutation.
/// Stacks are accumulated only via <see cref="AddSlashPoints"/>, which the
/// server-side slash handler calls when the Despoiler lands a hit on a marine.
///
/// Display is handled by the client-side XenoHudOverlay (drawn over the
/// Despoiler entity next to the health/plasma bars) — not by the
/// AlertsSystem.
/// </summary>
public sealed class XenoDespoilerHypertensionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public void AddSlashPoints(EntityUid uid, XenoDespoilerHypertensionComponent comp)
    {
        AddPoints(uid, comp, comp.PointsPerSlash);
    }

    public void AddPoints(EntityUid uid, XenoDespoilerHypertensionComponent comp, float amount)
    {
        if (amount <= 0)
            return;

        comp.Points += amount;
        comp.LastActivityAt = _timing.CurTime;

        while (comp.Points >= comp.PointsPerStack && comp.Stacks < comp.MaxStacks)
        {
            comp.Points -= comp.PointsPerStack;
            comp.Stacks++;
        }

        if (comp.Stacks >= comp.MaxStacks)
            comp.Points = 0;

        Dirty(uid, comp);
    }

    public bool TrySpendStacks(EntityUid uid, XenoDespoilerHypertensionComponent comp, int count)
    {
        if (comp.Stacks < count)
            return false;

        comp.Stacks -= count;
        Dirty(uid, comp);
        return true;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoDespoilerHypertensionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Stacks <= 0 && comp.Points <= 0)
                continue;

            var idleFor = (now - comp.LastActivityAt).TotalSeconds;
            if (idleFor < comp.DecayDelaySeconds)
                continue;

            var lost = comp.DecayPerSecond * frameTime;
            comp.Points -= lost;
            while (comp.Points < 0 && comp.Stacks > 0)
            {
                comp.Stacks--;
                comp.Points += comp.PointsPerStack;
            }

            if (comp.Stacks <= 0 && comp.Points < 0)
                comp.Points = 0;

            Dirty(uid, comp);
        }
    }
}
