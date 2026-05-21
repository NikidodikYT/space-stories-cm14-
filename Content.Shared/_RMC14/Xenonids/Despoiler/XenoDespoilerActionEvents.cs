using Content.Shared.Actions;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

public sealed partial class XenoDespoilerAcidBarrageActionEvent : WorldTargetActionEvent;

public sealed partial class XenoDespoilerCausticEmbraceActionEvent : WorldTargetActionEvent;

public sealed partial class XenoDespoilerOozingWoundsActionEvent : InstantActionEvent;

public sealed partial class XenoDespoilerCatalyzeActionEvent : InstantActionEvent;

/// <summary>
/// Sent from the client when the Despoiler operator presses LMB while Acid
/// Barrage is charging. Carries the cursor target so the server can spawn the
/// volley toward where the player clicked.
/// </summary>
[Serializable, NetSerializable]
public sealed class XenoDespoilerBarrageFireRequest : EntityEventArgs
{
    public NetCoordinates Target;
    public XenoDespoilerBarrageFireRequest(NetCoordinates target) => Target = target;
}

/// <summary>
/// Sent from the client when the Despoiler operator cancels the Acid Barrage charge.
/// </summary>
[Serializable, NetSerializable]
public sealed class XenoDespoilerBarrageCancelRequest : EntityEventArgs;
