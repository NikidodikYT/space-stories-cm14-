using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
///     Marker added when the Despoiler selects Acid Barrage. While present (replicated to the local client),
///     holding LMB charges the volley and releasing fires it; melee swings are suppressed.
///     Removed by the server after fire or cancel.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoDespoilerArmedBarrageComponent : Component;
