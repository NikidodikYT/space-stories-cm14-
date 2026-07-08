using Robust.Shared.GameStates;

namespace Content.Shared._Stories.Juggernaut;

/// <summary>M134C-JLCW recoil profile layered on top of GunToggleableRecoil + GunSpinup without forking either: re-applies a small spread over the smartgun's perfect-accuracy compensation, and kicks the camera along the raw aim direction when GunSpinup's scatter is too wild for the engine's own kick to read.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(JuggernautRecoilSystem))]
public sealed partial class JuggernautRecoilComponent : Component
{
    [DataField]
    public float CompensatedMinAngleDegrees = 10;

    [DataField]
    public float CompensatedMaxAngleDegrees = 15;

    [DataField]
    public float UncompensatedKickScale = 0.5f;
}
