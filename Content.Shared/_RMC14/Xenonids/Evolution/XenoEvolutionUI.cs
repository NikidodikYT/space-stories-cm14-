using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Xenonids.Evolution;

[Serializable, NetSerializable]
public enum XenoEvolutionUIKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class XenoEvolveBuiState(bool lackingOvipositor, List<EntProtoId> lotteryChoices) : BoundUserInterfaceState // Stories-Lottery
{
    public readonly bool LackingOvipositor = lackingOvipositor;

    // Stories-Lottery-Start
    /// <summary>
    /// This xeno's evolution targets that are decided by the pending tier lottery instead of a click race.
    /// Empty when no lottery is currently open for this xeno.
    /// </summary>
    public readonly List<EntProtoId> LotteryChoices = lotteryChoices;
    // Stories-Lottery-End
}

[Serializable, NetSerializable]
public sealed class XenoEvolveBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}

// Stories-Lottery-Start
[Serializable, NetSerializable]
public sealed class XenoLotteryRegisterBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}
// Stories-Lottery-End

[Serializable, NetSerializable]
public sealed class XenoStrainBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}
