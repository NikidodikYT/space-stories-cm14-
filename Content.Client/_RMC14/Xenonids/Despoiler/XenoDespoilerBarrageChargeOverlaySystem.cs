using System.Numerics;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerBarrageChargeOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        if (!_overlay.HasOverlay<XenoDespoilerBarrageChargeOverlay>())
            _overlay.AddOverlay(new XenoDespoilerBarrageChargeOverlay());
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay<XenoDespoilerBarrageChargeOverlay>();
    }
}

/// <summary>
///     World-space charge bar for the Acid Barrage. Drawn above every charging Despoiler's
///     sprite, in the same overlay space as the rest of the xeno HUD widgets.
///     Fills 0 → 1 over <see cref="XenoDespoilerChargingBarrageComponent.ExpiresAt"/> and
///     stays at 1 if the player keeps holding past the cap.
/// </summary>
public sealed class XenoDespoilerBarrageChargeOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ResPath BarSprite = new("/Textures/Interface/Misc/progress_bar.rsi");
    private const float StartX = 2f;
    private const float EndX = 22f;

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;

    private readonly EntityQuery<TransformComponent> _xformQuery;

    private readonly Texture _barTexture;
    private readonly ShaderInstance _unshaded;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public XenoDespoilerBarrageChargeOverlay()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entity.System<SpriteSystem>();
        _transform = _entity.System<TransformSystem>();
        _xformQuery = _entity.GetEntityQuery<TransformComponent>();
        _barTexture = _sprite.Frame0(new SpriteSpecifier.Rsi(BarSprite, "icon"));
        _unshaded = _proto.Index(UnshadedShader).Instance();
        ZIndex = 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var eyeRot = args.Viewport.Eye?.Rotation ?? default;
        var rotation = Matrix3Helpers.CreateRotation(-eyeRot);
        var localEnt = _player.LocalSession?.AttachedEntity;
        var now = _timing.CurTime;

        var query = _entity.AllEntityQueryEnumerator<XenoDespoilerChargingBarrageComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var charge, out var sprite, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var worldPos = _transform.GetWorldPosition(xform, _xformQuery);
            if (!args.WorldAABB.Enlarged(2f).Contains(worldPos))
                continue;

            handle.UseShader(uid == localEnt ? _unshaded : null);

            var world = Matrix3Helpers.CreateTranslation(worldPos);
            handle.SetTransform(Matrix3x2.Multiply(rotation, world));

            var alpha = sprite.Color.A;
            var yOffset = _sprite.GetLocalBounds((uid, sprite)).Height / 2f + 0.05f;
            var origin = new Vector2(-_barTexture.Width / 2f / EyeManager.PixelsPerMeter, yOffset);

            handle.DrawTexture(_barTexture, origin, Color.White.WithAlpha(alpha));

            var duration = (charge.ExpiresAt - charge.StartedAt).TotalSeconds;
            var ratio = duration > 0
                ? (float)Math.Clamp((now - charge.StartedAt).TotalSeconds / duration, 0, 1)
                : 1f;

            var xProgress = (EndX - StartX) * ratio + StartX;
            var fill = new Box2(
                new Vector2(StartX, 3f) / EyeManager.PixelsPerMeter,
                new Vector2(xProgress, 4f) / EyeManager.PixelsPerMeter)
                .Translated(origin);

            handle.DrawRect(fill, Color.FromHex("#7FFF00").WithAlpha(alpha));
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }
}
