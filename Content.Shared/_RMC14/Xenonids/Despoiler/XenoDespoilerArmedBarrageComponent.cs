using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
///     Marker added when the Despoiler selects Acid Barrage. While present, holding the
///     barrage-charge keybind starts charging a volley; releasing it fires.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoDespoilerArmedBarrageComponent : Component;
