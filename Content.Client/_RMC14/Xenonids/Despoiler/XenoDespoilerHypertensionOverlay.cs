using System.Numerics;
using Content.Shared._RMC14.Mobs;
using Content.Shared._RMC14.Stealth;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Shared.Utility.SpriteSpecifier;

namespace Content.Client._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerHypertensionOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly ContainerSystem _container;
    private readonly MobStateSystem _mobState;
    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;

    private readonly EntityQuery<TransformComponent> _xformQuery;
    private readonly EntityQuery<MobStateComponent> _mobStateQuery;
    private readonly EntityQuery<EntityActiveInvisibleComponent> _invisQuery;

    private readonly ResPath _rsiPath = new("/Textures/_RMC14/Interface/Alerts/hypertension.rsi");

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public XenoDespoilerHypertensionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _container = _entity.System<ContainerSystem>();
        _mobState = _entity.System<MobStateSystem>();
        _sprite = _entity.System<SpriteSystem>();
        _transform = _entity.System<TransformSystem>();
        _xformQuery = _entity.GetEntityQuery<TransformComponent>();
        _mobStateQuery = _entity.GetEntityQuery<MobStateComponent>();
        _invisQuery = _entity.GetEntityQuery<EntityActiveInvisibleComponent>();
        ZIndex = 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var local = _players.LocalEntity;
        var isAdminGhost = _entity.TryGetComponent(local, out GhostComponent? ghost) && ghost.CanGhostInteract;
        var isXeno = _entity.HasComponent<XenoComponent>(local);
        var isXenoGhost = _entity.HasComponent<CMGhostXenoHudComponent>(local);

        if (!isXeno && !isAdminGhost && !isXenoGhost)
            return;

        var handle = args.WorldHandle;
        var eyeRot = args.Viewport.Eye?.Rotation ?? default;
        var rotationMatrix = Matrix3Helpers.CreateRotation(-eyeRot);

        var query = _entity
            .AllEntityQueryEnumerator<XenoDespoilerHypertensionComponent, XenoComponent, SpriteComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var hyper, out var xeno, out var sprite, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            if (_container.IsEntityOrParentInContainer(uid, xform: xform))
                continue;

            if (_invisQuery.HasComp(uid))
                continue;

            if (_mobStateQuery.TryComp(uid, out var mobState) && _mobState.IsDead(uid, mobState))
                continue;

            var bounds = sprite.Bounds;
            var worldPos = _transform.GetWorldPosition(xform, _xformQuery);

            if (!bounds.Translated(worldPos).Intersects(args.WorldAABB))
                continue;

            var worldMatrix = Matrix3x2.CreateTranslation(worldPos);
            var matrix = Matrix3x2.Multiply(rotationMatrix, worldMatrix);
            handle.SetTransform(matrix);

            var level = Math.Clamp(hyper.Stacks, 0, 4);
            var icon = new Rsi(_rsiPath, $"level_{level}");
            var texture = _sprite.GetFrame(icon, _timing.CurTime);

            var yOffset = (bounds.Height + sprite.Offset.Y) / 2f
                          - (float)texture.Height / EyeManager.PixelsPerMeter * bounds.Height
                          + xeno.HudOffset.Y;
            var xOffset = (bounds.Width + sprite.Offset.X) / 2f
                          - (float)texture.Width / EyeManager.PixelsPerMeter * bounds.Width
                          + xeno.HudOffset.X
                          + (float)texture.Width / EyeManager.PixelsPerMeter;

            handle.DrawTexture(texture, new Vector2(xOffset, yOffset));
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
