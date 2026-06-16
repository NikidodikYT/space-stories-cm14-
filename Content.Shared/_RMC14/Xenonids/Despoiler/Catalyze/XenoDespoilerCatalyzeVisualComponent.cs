namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
///     Tracks the spawned Catalyze empower visual so it can be despawned when the
///     empowerment ends. Server-authoritative effect state.
/// </summary>
[RegisterComponent]
public sealed partial class XenoDespoilerCatalyzeVisualComponent : Component
{
    public EntityUid? CatalyzeVisual;
}
