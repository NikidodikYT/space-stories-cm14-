using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.ActionBlocker;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._RMC14.Xenonids.Despoiler;

/// <summary>
///     Dynamic-bind variant of the old global keybind. The Use-keybind is registered
///     ONLY while the local player has either <see cref="XenoDespoilerArmedBarrageComponent"/>
///     or <see cref="XenoDespoilerChargingBarrageComponent"/>, and unregistered the
///     moment both come off. In every other moment of the game — and for every player
///     who is not a Despoiler that just selected the ability — the handler does not
///     exist in the engine's command pipeline at all.
/// </summary>
public sealed class XenoDespoilerBarrageInputSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private EntityQuery<XenoDespoilerArmedBarrageComponent> _armedQuery;
    private EntityQuery<XenoDespoilerChargingBarrageComponent> _chargingQuery;

    private bool _bindRegistered;

    public override void Initialize()
    {
        _armedQuery = GetEntityQuery<XenoDespoilerArmedBarrageComponent>();
        _chargingQuery = GetEntityQuery<XenoDespoilerChargingBarrageComponent>();

        SubscribeLocalEvent<XenoDespoilerArmedBarrageComponent, ComponentStartup>(OnLifecycleChange);
        SubscribeLocalEvent<XenoDespoilerArmedBarrageComponent, ComponentShutdown>(OnLifecycleChange);
        SubscribeLocalEvent<XenoDespoilerChargingBarrageComponent, ComponentStartup>(OnLifecycleChange);
        SubscribeLocalEvent<XenoDespoilerChargingBarrageComponent, ComponentShutdown>(OnLifecycleChange);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    public override void Shutdown()
    {
        UnregisterBind();
    }

    private void OnLifecycleChange<T>(EntityUid uid, T comp, object args) where T : IComponent
    {
        if (_player.LocalEntity == uid)
            RefreshBind();
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent args)
    {
        RefreshBind();
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        UnregisterBind();
    }

    private void RefreshBind()
    {
        if (_player.LocalEntity is not { } ent)
        {
            UnregisterBind();
            return;
        }

        var shouldBind = _armedQuery.HasComp(ent) || _chargingQuery.HasComp(ent);
        if (shouldBind)
            EnsureBind();
        else
            UnregisterBind();
    }

    private void EnsureBind()
    {
        if (_bindRegistered)
            return;

        CommandBinds.Builder
            .Bind(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnUse, outsidePrediction: true))
            .Register<XenoDespoilerBarrageInputSystem>();
        _bindRegistered = true;
    }

    private void UnregisterBind()
    {
        if (!_bindRegistered)
            return;

        CommandBinds.Unregister<XenoDespoilerBarrageInputSystem>();
        _bindRegistered = false;
    }

    private bool OnUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (_player.LocalEntity is not { } ent)
            return false;

        if (!_actionBlocker.CanConsciouslyPerformAction(ent))
            return false;

        var charging = _chargingQuery.HasComp(ent);
        var armed = _armedQuery.HasComp(ent);

        if (args.State == BoundKeyState.Down && armed && !charging)
        {
            if (!args.Coordinates.IsValid(EntityManager))
                return false;

            RaiseNetworkEvent(new XenoDespoilerBarrageStartChargeRequest(GetNetCoordinates(args.Coordinates)));
            return true;
        }

        if (args.State == BoundKeyState.Up && charging)
        {
            if (!TryGetCursorCoords(out var coords))
                return false;

            RaiseNetworkEvent(new XenoDespoilerBarrageFireRequest(GetNetCoordinates(coords)));
            return true;
        }

        return false;
    }

    private bool TryGetCursorCoords(out EntityCoordinates coords)
    {
        coords = default;
        var mousePos = _eye.PixelToMap(_input.MouseScreenPosition);

        EntityUid grid;
        if (_mapManager.TryFindGridAt(mousePos, out var gridUid, out _))
            grid = gridUid;
        else if (_map.TryGetMap(mousePos.MapId, out var map))
            grid = map.Value;
        else
            return false;

        coords = _transform.ToCoordinates(grid, mousePos);
        return true;
    }
}
