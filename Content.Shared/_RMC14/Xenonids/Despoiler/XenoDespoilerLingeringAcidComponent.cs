using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class XenoDespoilerLingeringAcidComponent : Component
{
    [DataField]
    public TimeSpan MinLifetime = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan MaxLifetime = TimeSpan.FromSeconds(20);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan ExpiresAt;

    [DataField]
    public float CrossBurnDamage = 20f;

    [DataField, AutoNetworkedField]
    public EntityUid? Caster;
}
