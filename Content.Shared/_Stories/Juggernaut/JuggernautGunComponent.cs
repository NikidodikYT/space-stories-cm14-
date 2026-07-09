using Robust.Shared.GameStates;

namespace Content.Shared._Stories.Juggernaut;

/// <summary>Marks the M134C-JLCW itself - distinct from <see cref="JuggernautWhitelistComponent"/>, which marks the player who has the Juggernaut specialist skill. Lets JuggernautBatterySystem lock its power cell and JuggernautAssistedRepairSystem find it in the wielder's hands.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(JuggernautBatterySystem), typeof(JuggernautAssistedRepairSystem))]
public sealed partial class JuggernautGunComponent : Component;
