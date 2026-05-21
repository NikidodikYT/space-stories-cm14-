using Robust.Client.Graphics;

namespace Content.Client._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerHypertensionHudSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        if (!_overlay.HasOverlay<XenoDespoilerHypertensionOverlay>())
            _overlay.AddOverlay(new XenoDespoilerHypertensionOverlay());
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay<XenoDespoilerHypertensionOverlay>();
    }
}
