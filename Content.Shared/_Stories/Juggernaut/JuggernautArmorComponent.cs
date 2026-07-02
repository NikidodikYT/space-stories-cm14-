using Content.Shared.Inventory;
using Robust.Shared.GameStates;

namespace Content.Shared._Stories.Juggernaut;

/// <summary>
///     Exoskeleton armor required to fire the M134C. Slows the wearer down after standing up.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(JuggernautSystem))]
public sealed partial class JuggernautArmorComponent : Component
{
    [DataField, AutoNetworkedField]
    public SlotFlags Slots = SlotFlags.OUTERCLOTHING;

    [DataField, AutoNetworkedField]
    public TimeSpan StandUpSlowdown = TimeSpan.FromSeconds(3);
}
