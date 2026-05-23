using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.ActionBlocker;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Content.Client._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerBarrageKeybindSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private EntityQuery<XenoDespoilerComponent> _despoilerQuery;
    private EntityQuery<XenoDespoilerArmedBarrageComponent> _armedQuery;
    private EntityQuery<XenoDespoilerChargingBarrageComponent> _chargingQuery;

    public override void Initialize()
    {
        _despoilerQuery = GetEntityQuery<XenoDespoilerComponent>();
        _armedQuery = GetEntityQuery<XenoDespoilerArmedBarrageComponent>();
        _chargingQuery = GetEntityQuery<XenoDespoilerChargingBarrageComponent>();

        CommandBinds.Builder
            .Bind(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnUseDown, outsidePrediction: true))
            .Register<XenoDespoilerBarrageKeybindSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<XenoDespoilerBarrageKeybindSystem>();
    }

    private bool OnUseDown(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State != BoundKeyState.Down)
            return false;

        if (_player.LocalEntity is not { } ent)
            return false;

        if (!_despoilerQuery.HasComp(ent) || !_armedQuery.HasComp(ent))
            return false;

        if (!_actionBlocker.CanConsciouslyPerformAction(ent))
            return false;

        if (!args.Coordinates.IsValid(EntityManager))
            return false;

        RaiseNetworkEvent(new XenoDespoilerBarrageStartChargeRequest(GetNetCoordinates(args.Coordinates)));
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_player.LocalEntity is not { } ent)
            return;

        if (!_chargingQuery.HasComp(ent))
            return;

        if (_inputSystem.CmdStates.GetState(EngineKeyFunctions.Use) == BoundKeyState.Down)
            return;

        if (!TryGetCursorCoords(out var coords))
            return;

        RaiseNetworkEvent(new XenoDespoilerBarrageFireRequest(GetNetCoordinates(coords)));
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
