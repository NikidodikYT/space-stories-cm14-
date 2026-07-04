using System.Numerics;
using Content.Shared._RMC14.Fishing;
using Robust.Client.GameObjects;

namespace Content.Client._RMC14.Sprite;

public sealed class ScaleVisualsSystem : VisualizerSystem<ScaleVisualsComponent>
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    protected override void OnAppearanceChange(EntityUid uid, ScaleVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (AppearanceSystem.TryGetData(uid, ScaleVisuals.Scale, out Vector2 scale, args.Component))
        {
            _sprite.SetScale((uid, args.Sprite), scale);
        }
    }
}
