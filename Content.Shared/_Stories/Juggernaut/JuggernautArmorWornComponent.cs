using Robust.Shared.GameStates;

namespace Content.Shared._Stories.Juggernaut;

/// <summary>Granted by ClothingGrantComponents while the armor is worn - keys the drag slowdown, slow get-up and tackle resistance to actually wearing the armor, unlike <see cref="JuggernautWhitelistComponent"/> which marks the specialist regardless of gear.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class JuggernautArmorWornComponent : Component
{
    [DataField]
    public TimeSpan GetUpDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public int TackleResistMin = 5;
}
