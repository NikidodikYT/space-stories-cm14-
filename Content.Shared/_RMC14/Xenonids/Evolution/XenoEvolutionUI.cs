using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Xenonids.Evolution;

[Serializable, NetSerializable]
public enum XenoEvolutionUIKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class XenoEvolveBuiState(bool lackingOvipositor, List<EntProtoId> queueChoices) : BoundUserInterfaceState // Stories-EvoQueue
{
    public readonly bool LackingOvipositor = lackingOvipositor;

    // Stories-EvoQueue: tier-limited castes this xeno enters the hive evolution queue for instead of evolving directly.
    public readonly List<EntProtoId> QueueChoices = queueChoices;
}

[Serializable, NetSerializable]
public sealed class XenoEvolveBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}

// Stories-EvoQueue
[Serializable, NetSerializable]
public sealed class XenoEvolutionQueueCancelBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class XenoStrainBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}
