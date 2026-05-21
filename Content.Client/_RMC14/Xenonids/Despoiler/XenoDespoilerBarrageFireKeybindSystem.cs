using Content.Shared.ActionBlocker;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Numerics;

namespace Content.Client._RMC14.Xenonids.Despoiler;

/// <summary>
/// Mouse dispatcher for the Acid Barrage charge UX.
///
///   LMB while the local entity holds <see cref="XenoDespoilerChargingBarrageComponent"/>:
///     fire the volley at the click coordinates
///     (<see cref="XenoDespoilerBarrageFireRequest"/>) and consume the input so
///     it doesn't fall through to normal interaction / attack.
///   RMB while charging: cancel the charge
///     (<see cref="XenoDespoilerBarrageCancelRequest"/>) and consume the input.
///   When not charging both clicks are passed through unchanged.
/// </summary>
public sealed class XenoDespoilerBarrageFireKeybindSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnLeftClick))
            .Bind(EngineKeyFunctions.UseSecondary, new PointerInputCmdHandler(OnRightClick))
            .Register<XenoDespoilerBarrageFireKeybindSystem>();

        _overlayManager.AddOverlay(new XenoDespoilerBarrageChargeOverlay(EntityManager, _prototypeManager, _timing, _playerManager));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<XenoDespoilerBarrageFireKeybindSystem>();
        _overlayManager.RemoveOverlay<XenoDespoilerBarrageChargeOverlay>();
    }

    private bool OnLeftClick(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State != BoundKeyState.Down)
            return false;

        var ent = _playerManager.LocalEntity;
        if (ent == null || !HasComp<XenoDespoilerComponent>(ent.Value))
            return false;

        if (!HasComp<XenoDespoilerChargingBarrageComponent>(ent.Value))
            return false;

        if (!_actionBlocker.CanConsciouslyPerformAction(ent.Value))
            return false;

        if (!args.Coordinates.IsValid(EntityManager))
            return false;

        RaiseNetworkEvent(new XenoDespoilerBarrageFireRequest(GetNetCoordinates(args.Coordinates)));
        return true;
    }

    private bool OnRightClick(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State != BoundKeyState.Down)
            return false;

        var ent = _playerManager.LocalEntity;
        if (ent == null || !HasComp<XenoDespoilerComponent>(ent.Value))
            return false;

        if (!HasComp<XenoDespoilerChargingBarrageComponent>(ent.Value))
            return false;

        RaiseNetworkEvent(new XenoDespoilerBarrageCancelRequest());
        return true;
    }
}

public sealed class XenoDespoilerBarrageChargeOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    private readonly IEntityManager _entManager;
    private readonly IGameTiming _timing;
    private readonly IPlayerManager _player;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;

    private readonly Texture _barTexture;
    private readonly ShaderInstance _unshadedShader;

    private const float StartX = 2f;
    private const float EndX = 22f;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public XenoDespoilerBarrageChargeOverlay(IEntityManager entManager, IPrototypeManager protoManager, IGameTiming timing, IPlayerManager player)
    {
        _entManager = entManager;
        _timing = timing;
        _player = player;
        _transform = _entManager.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
        _sprite = _entManager.System<SpriteSystem>();
        var sprite = new SpriteSpecifier.Rsi(new("/Textures/Interface/Misc/progress_bar.rsi"), "icon");
        _barTexture = _sprite.Frame0(sprite);
        _unshadedShader = protoManager.Index(UnshadedShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var rotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();

        const float scale = 1f;
        var scaleMatrix = Matrix3Helpers.CreateScale(new Vector2(scale, scale));
        var rotationMatrix = Matrix3Helpers.CreateRotation(-rotation);

        var curTime = _timing.CurTime;
        var bounds = args.WorldAABB.Enlarged(5f);
        var localEnt = _player.LocalSession?.AttachedEntity;

        var enumerator = _entManager.AllEntityQueryEnumerator<XenoDespoilerChargingBarrageComponent, SpriteComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var charge, out var sprite, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var worldPosition = _transform.GetWorldPosition(xform, xformQuery);
            if (!bounds.Contains(worldPosition))
                continue;

            if (uid != localEnt)
                handle.UseShader(null);
            else
                handle.UseShader(_unshadedShader);

            var worldMatrix = Matrix3Helpers.CreateTranslation(worldPosition);
            var scaledWorld = Matrix3x2.Multiply(scaleMatrix, worldMatrix);
            var matty = Matrix3x2.Multiply(rotationMatrix, scaledWorld);
            handle.SetTransform(matty);

            var alpha = sprite.Color.A;

            var yOffset = _sprite.GetLocalBounds((uid, sprite)).Height / 2f + 0.05f;
            var position = new Vector2(-_barTexture.Width / 2f / EyeManager.PixelsPerMeter,
                yOffset / scale);

            handle.DrawTexture(_barTexture, position, Color.White.WithAlpha(alpha));

            var elapsed = curTime - charge.StartedAt;
            var duration = charge.ExpiresAt - charge.StartedAt;
            var elapsedRatio = duration.TotalSeconds > 0
                ? Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds, 0.0, 1.0)
                : 0.0;

            var color = Color.FromHex("#7FFF00").WithAlpha(alpha); // lime green

            var xProgress = (EndX - StartX) * (float)elapsedRatio + StartX;
            var box = new Box2(new Vector2(StartX, 3f) / EyeManager.PixelsPerMeter, new Vector2(xProgress, 4f) / EyeManager.PixelsPerMeter);
            box = box.Translated(position);
            handle.DrawRect(box, color);
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }
}
