using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Evolution;

// Stories-EvoQueue
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEvolutionSystem))]
public sealed partial class XenoEvolutionQueueComponent : Component
{
    // Round time the xeno entered its current tier; lower = waited longer = higher priority.
    [DataField]
    public TimeSpan TierEnteredAt;

    // Set while a slot is reserved for this xeno (the "pending evolution" list); the deadline to evolve.
    [DataField, AutoNetworkedField]
    public TimeSpan? OfferedUntil;

    // Tier the active offer (or in-progress evolution) reserves a slot for.
    [DataField]
    public int OfferedTier;

    // Set while a clicked evolution channels into a reserved slot (offer already consumed); keeps the slot reserved until this round time.
    [DataField]
    public TimeSpan? EvolvingUntil;

    // After declining/timing out, sit out until this round time so the offer passes down the queue.
    [DataField]
    public TimeSpan? PassedUntil;
}
