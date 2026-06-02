using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Evolution;

// Stories-EvoQueue
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEvolutionSystem))]
public sealed partial class XenoEvolutionQueueComponent : Component
{
    [DataField]
    public TimeSpan TierEnteredAt;

    [DataField, AutoNetworkedField]
    public TimeSpan? OfferedUntil;

    [DataField]
    public int OfferedTier;

    [DataField]
    public TimeSpan? EvolvingUntil;

    [DataField]
    public TimeSpan? PassedUntil;
}
