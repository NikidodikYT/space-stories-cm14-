using Content.Shared.Movement.Systems;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Pure speed-modifier hook for the Acid Barrage charging state.
/// Lives in shared because RefreshMovementSpeedModifiersEvent must be handled
/// on both sides to avoid client-prediction desync. Does not spawn or damage.
/// </summary>
public sealed class XenoDespoilerBarrageChargeSpeedSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerChargingBarrageComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
        SubscribeLocalEvent<XenoDespoilerChargingBarrageComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<XenoDespoilerChargingBarrageComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(EntityUid uid, XenoDespoilerChargingBarrageComponent comp, ComponentStartup args)
    {
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, XenoDespoilerChargingBarrageComponent comp, ComponentShutdown args)
    {
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefresh(EntityUid uid, XenoDespoilerChargingBarrageComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        // ComponentShutdown raises this same event with the comp still on the
        // entity. Without this guard the modifier would re-apply during the
        // very tick we're trying to clear it, leaving the despoiler slowed
        // forever after a volley.
        if (comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        var mult = comp.SpeedMultiplier <= 0 ? 0.5f : comp.SpeedMultiplier;
        args.ModifySpeed(mult, mult);
    }

    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
}
