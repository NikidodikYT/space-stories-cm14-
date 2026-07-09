using Robust.Shared.GameStates;

namespace Content.Shared._Stories.Juggernaut;

/// <summary>Granted by ClothingGrantComponents while the armor is worn - keys the slow get-up and tackle resistance to actually wearing the armor, unlike <see cref="JuggernautWhitelistComponent"/> which marks the specialist regardless of gear. Drag slowdown is a plain SlowOnPull entry in base_xeno.yml keyed on this component, not C#.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(JuggernautGetUpSystem), typeof(JuggernautTackleResistSystem))]
public sealed partial class JuggernautArmorWornComponent : Component
{
    [DataField]
    public TimeSpan GetUpDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public int TackleResistMin = 5;
}
