using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Marks the Despoiler as "every melee slash applies/extends Acid".
/// Configuration only — server-side hit handler lives in
/// <c>XenoDespoilerAcidSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerSlashOnHitComponent : Component
{
    [DataField, AutoNetworkedField]
    public int EnhanceStacksThreshold = 3;

    [DataField, AutoNetworkedField]
    public int AcidArmorPierce = 10;

    [DataField, AutoNetworkedField]
    public int AcidLevel3MeleeBioDebuff = 15;

    [DataField, AutoNetworkedField]
    public float AcidApplyDuration = 12f;
}
