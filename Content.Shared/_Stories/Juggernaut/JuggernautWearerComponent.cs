using Robust.Shared.GameStates;

namespace Content.Shared._Stories.Juggernaut;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(JuggernautSystem))]
public sealed partial class JuggernautWearerComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan StandUpSlowdown = TimeSpan.FromSeconds(3);
}
