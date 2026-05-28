using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Xenonids.Evolution;

[Serializable, NetSerializable]
public enum XenoEvolutionUIKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class XenoEvolveBuiState(bool lackingOvipositor, bool lotteryOpen, List<EntProtoId> lotteryChoices) : BoundUserInterfaceState
{
    public readonly bool LackingOvipositor = lackingOvipositor;

    /// <summary>
    /// Whether a tier evolution lottery this xeno can register for is currently pending.
    /// </summary>
    public readonly bool LotteryOpen = lotteryOpen;

    /// <summary>
    /// This xeno's evolution targets that are drawn through the pending lottery instead of a click race.
    /// </summary>
    public readonly List<EntProtoId> LotteryChoices = lotteryChoices;
}

[Serializable, NetSerializable]
public sealed class XenoEvolveBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}

[Serializable, NetSerializable]
public sealed class XenoLotteryRegisterBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}

[Serializable, NetSerializable]
public sealed class XenoStrainBuiMsg(EntProtoId choice) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Choice = choice;
}
