using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerSlashOnHitComponent : Component
{
    [DataField, AutoNetworkedField]
    public int EnhanceStacksThreshold = 2;

    [DataField, AutoNetworkedField]
    public TimeSpan AcidApplyDuration = TimeSpan.FromSeconds(12);

    [DataField, AutoNetworkedField]
    public int AcidArmorPiercing = 40;

    [DataField, AutoNetworkedField]
    public DamageSpecifier AcidTickDamage = new()
    {
        DamageDict = { ["Heat"] = 1 },
    };
}
