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

/// <summary>
///     Hold-LMB-to-charge / release-to-fire UX for the Despoiler's Acid Barrage.
///     Down is intercepted with a <see cref="PointerInputCmdHandler"/> so the normal
///     LMB attack does not fire while the action is armed. The release is detected via
///     per-tick polling of <see cref="InputSystem.CmdStates"/> because the Up edge of
///     a pointer handler can be consumed by other systems before it reaches us.
/// </summary>
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

    public override void Initialize()
    {
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
        // We only intercept the *Down* edge to suppress the normal LMB attack while armed.
        // Up + auto-fire flow lives in Update.
        if (args.State != BoundKeyState.Down)
            return false;

        if (_player.LocalEntity is not { } ent)
            return false;

        if (!HasComp<XenoDespoilerComponent>(ent))
            return false;

        if (!HasComp<XenoDespoilerArmedBarrageComponent>(ent))
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

        if (!HasComp<XenoDespoilerChargingBarrageComponent>(ent))
            return;

        // Fire strictly on release. The player can keep holding past the cap; the bar just
        // stays at 100% and only the release sends a FireRequest.
        if (_inputSystem.CmdStates.GetState(EngineKeyFunctions.Use) == BoundKeyState.Down)
            return;

        if (!TryGetCursorCoords(out var coords))
            return;

        // The server clears Charging in response, so the next tick will short-circuit at HasComp.
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
