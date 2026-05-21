using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// On the projectile entity. Read by the server on hit.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerAcidBarrageProjectileComponent : Component
{
    [DataField, AutoNetworkedField]
    public int SplashRadius = 1;

    [DataField, AutoNetworkedField]
    public float LingeringAcidChance = 0.25f;

    [DataField, AutoNetworkedField]
    public bool EnhanceAcid = true;

    [DataField, AutoNetworkedField]
    public EntityUid? Shooter;

    /// <summary>
    /// Per-projectile random scale factor [0.9..1.33], mirroring CM13's
    /// scale_matrix.Scale(factor, factor) on Acid Barrage shots.
    /// Applied to the SpriteComponent on the client.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Scale = Vector2.One;
}

/// <summary>
/// Lingering Acid puddle. Decays on a randomized 15-20s window set at spawn.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerLingeringAcidComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MinLifetimeSeconds = 15f;

    [DataField, AutoNetworkedField]
    public float MaxLifetimeSeconds = 20f;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;

    [DataField, AutoNetworkedField]
    public float CrossBurnDamage = 20f;

    [DataField, AutoNetworkedField]
    public float SlowdownSeconds = 1.5f;

    [DataField, AutoNetworkedField]
    public EntityUid? Owner;
}

/// <summary>
/// Acid Spray (Oozing Wounds). Hits mobs that cross during its TimedDespawn lifetime.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerAcidSprayComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Damage = 30f;

    [DataField, AutoNetworkedField]
    public bool StunsOnEmpowered;

    [DataField, AutoNetworkedField]
    public float StunSeconds = 1f;

    [DataField, AutoNetworkedField]
    public float GrantImmunitySeconds = 3f;

    [DataField, AutoNetworkedField]
    public EntityUid? Owner;
}
