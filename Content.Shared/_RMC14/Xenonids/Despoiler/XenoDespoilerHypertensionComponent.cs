using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Despoiler's Hypertension resource (state only). Logic lives in
/// <c>XenoDespoilerHypertensionSystem</c> (shared, read-only accumulation) and
/// <c>XenoDespoilerAcidSystem</c> (slash bonus damage).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerHypertensionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int MaxStacks = 4;

    [DataField, AutoNetworkedField]
    public int Stacks;

    [DataField, AutoNetworkedField]
    public float Points;

    [DataField, AutoNetworkedField]
    public float PointsPerStack = 200f;

    [DataField, AutoNetworkedField]
    public float PointsPerSlash = 100f;

    [DataField, AutoNetworkedField]
    public float DecayDelaySeconds = 10f;

    [DataField, AutoNetworkedField]
    public float DecayPerSecond = 200f;

    [DataField, AutoNetworkedField]
    public float BonusBurnPerStack = 5f;

    [DataField, AutoNetworkedField]
    public TimeSpan LastActivityAt;
}
