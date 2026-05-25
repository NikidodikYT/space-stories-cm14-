using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
///     Marker added when the Despoiler selects Acid Barrage. While present (replicated to the local client),
///     the client dynamically registers a Use-keybind to start the charge on LMB-down and fire on LMB-up.
///     Removed by the server after fire or cancel; the client's dynamic bind unregisters itself when this
///     component disappears.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoDespoilerArmedBarrageComponent : Component;
