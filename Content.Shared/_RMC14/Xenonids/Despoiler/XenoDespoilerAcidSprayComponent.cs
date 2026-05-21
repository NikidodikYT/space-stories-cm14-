using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerAcidSprayComponent : Component
{
    [DataField]
    public float Damage = 30f;

    [DataField, AutoNetworkedField]
    public bool StunsOnEmpowered;

    [DataField, AutoNetworkedField]
    public TimeSpan StunDuration = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public TimeSpan GrantImmunityDuration = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public EntityUid? Caster;
}
