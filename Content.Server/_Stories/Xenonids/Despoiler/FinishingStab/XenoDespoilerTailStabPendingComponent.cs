using Robust.Shared.Timing;

namespace Content.Server._Stories.Xenonids.Despoiler;

/// <summary>
///     One-shot marker set during RMCGetTailStabBonusDamageEvent and consumed by the
///     tail stab's own MeleeHitEvent on the same tick, so finishing-stab bonus damage is
///     applied exactly once to the entity that triggered the tail stab.
/// </summary>
[RegisterComponent]
public sealed partial class XenoDespoilerTailStabPendingComponent : Component
{
    // Tick the marker was set; only a hit on that same tick consumes it.
    public GameTick Tick;
}
