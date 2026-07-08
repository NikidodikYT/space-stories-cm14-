using Content.Shared.Whitelist;

namespace Content.Shared._Stories.Juggernaut;

/// <summary>A power cell locked to specific guns - configurable version of SmartGunSystem's hardcoded smartgun/battery lock.</summary>
[RegisterComponent]
[Access(typeof(JuggernautBatterySystem))]
public sealed partial class JuggernautBatteryComponent : Component
{
    [DataField(required: true)]
    public EntityWhitelist Guns = new();
}
