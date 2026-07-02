using Content.Shared._RMC14.Marines.Skills;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Stories.Juggernaut;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(JuggernautSystem))]
public sealed partial class JuggernautGunComponent : Component
{
    /// <summary>
    ///     Total damage at which the gun can't shoot until it gets welded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 BrokenThreshold = FixedPoint2.New(50);

    [DataField, AutoNetworkedField]
    public string MagazineSlotId = "gun_magazine";

    [DataField, AutoNetworkedField]
    public TimeSpan ReloadDelay = TimeSpan.FromSeconds(9);

    /// <summary>
    ///     Reload delay used when a skilled assistant is standing next to the shooter.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan AssistedReloadDelay = TimeSpan.FromSeconds(4.5);

    [DataField, AutoNetworkedField]
    public float AssistRange = 2f;

    [DataField, AutoNetworkedField]
    public EntProtoId<SkillDefinitionComponent> AssistSkill = "RMCSkillEngineer";

    [DataField, AutoNetworkedField]
    public int AssistSkillLevel = 1;

    public bool Inserting;

    public TimeSpan LastBrokenPopupAt;
}
