using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Applied to victims by Despoiler slashes / projectiles. Level scales 1..3.
/// DoT and armor-debuff handling lives in <c>XenoDespoilerAcidSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerAcidEffectComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Level = 1;

    [DataField, AutoNetworkedField]
    public int MaxLevel = 3;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;

    [DataField, AutoNetworkedField]
    public float DurationSeconds = 12f;

    [DataField, AutoNetworkedField]
    public TimeSpan NextTickAt;

    [DataField, AutoNetworkedField]
    public float TickIntervalSeconds = 2f;

    [DataField, AutoNetworkedField]
    public float DamagePerTickPerLevel = 2f;

    [DataField, AutoNetworkedField]
    public int MeleeArmorDebuffAtMaxLevel = 15;

    [DataField, AutoNetworkedField]
    public int BioArmorDebuffAtMaxLevel = 15;
}
