using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Xenonids.Evolution;

/// <summary>
/// Marks a xeno as registered for the one-time tier evolution lottery, targeting <see cref="Target"/>.
/// Added/removed by <see cref="XenoEvolutionSystem"/> while the lottery for the target's tier is pending.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEvolutionSystem))]
public sealed partial class XenoLotteryRegistrationComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId Target;
}
