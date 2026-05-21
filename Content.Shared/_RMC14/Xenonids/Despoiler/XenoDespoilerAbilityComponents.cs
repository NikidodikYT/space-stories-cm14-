using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Xenonids.Despoiler;



[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerAcidBarrageActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int PlasmaCost = 100;

    [DataField, AutoNetworkedField]
    public float MaxChargeSeconds = 3f;

    [DataField, AutoNetworkedField]
    public int MinProjectiles = 1;

    [DataField, AutoNetworkedField]
    public int MaxProjectiles = 8;

    [DataField, AutoNetworkedField]
    public int EmpowerBonusProjectiles = 6;

    [DataField, AutoNetworkedField]
    public float ScatterDegrees = 30f;

    [DataField, AutoNetworkedField]
    public float ChargingSpeedMultiplier = 0.5f;

    [DataField, AutoNetworkedField]
    public EntProtoId ProjectileId = "RMCProjectileDespoilerAcidShot";

    [DataField, AutoNetworkedField]
    public float LingeringAcidChance = 0.25f;

    [DataField, AutoNetworkedField]
    public int SplashRadius = 1;

    /// <summary>Tiles/second velocity passed to <c>SharedGunSystem.ShootProjectile</c>.</summary>
    [DataField, AutoNetworkedField]
    public float ProjectileSpeed = 12f;

    /// <summary>Min/max travel distance per shot in tiles. CM13: rand(1, 6).</summary>
    [DataField, AutoNetworkedField]
    public int MinRangeTiles = 1;

    [DataField, AutoNetworkedField]
    public int MaxRangeTiles = 6;

    /// <summary>Random scale range per projectile. CM13: rand(0.9, 1.33).</summary>
    [DataField, AutoNetworkedField]
    public float MinProjectileScale = 0.9f;

    [DataField, AutoNetworkedField]
    public float MaxProjectileScale = 1.33f;

    /// <summary>
    /// Cooldown applied when the operator cancels mid-charge via the action
    /// button. Prevents start-cancel-start spam without burning the full
    /// useDelay every time. Zero disables.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CancelCooldownSeconds = 2f;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ChargeSound;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? FireSound;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerCausticEmbraceActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int PlasmaCost = 100;

    [DataField, AutoNetworkedField]
    public int NormalRange = 1;

    [DataField, AutoNetworkedField]
    public int EmpoweredRange = 5;

    /// <summary>Damage dropped on every tile of the U-shape splash.</summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier SplashDamage = new()
    {
        DamageDict = { ["Heat"] = 30 },
    };

    /// <summary>Damage dealt to the single victim in empowered mode.</summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier EmpoweredDamage = new()
    {
        DamageDict = { ["Heat"] = 30 },
    };

    [DataField, AutoNetworkedField]
    public float LingeringAcidChance = 0.3f;

    [DataField, AutoNetworkedField]
    public float EmpoweredWeakenSeconds = 1f;

    [DataField, AutoNetworkedField]
    public float SplashAcidDurationSeconds = 12f;

    [DataField, AutoNetworkedField]
    public EntProtoId TelegraphProto = "RMCEffectDespoilerCausticTelegraph";

    [DataField, AutoNetworkedField]
    public EntProtoId LingeringAcidProto = "RMCEffectDespoilerLingeringAcid";

    /// <summary>
    /// Components added to the empowered-lunge victim — typically a yellow
    /// (combo) UserAcided so the kill plays out like Acider Runner's
    /// XenoAcidSlash combo hit instead of a one-shot stun.
    /// </summary>
    [DataField]
    public ComponentRegistry? EmpoweredAcid;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? PounceSound;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerOozingWoundsActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int PlasmaCost = 100;

    [DataField, AutoNetworkedField]
    public int BaseRadius = 1;

    [DataField, AutoNetworkedField]
    public float SeverityHpThreshold1 = 0.7f;

    [DataField, AutoNetworkedField]
    public float SeverityHpThreshold2 = 0.3f;

    [DataField, AutoNetworkedField]
    public float SprayLifetime = 2f;

    [DataField, AutoNetworkedField]
    public float SprayDamage = 30f;

    [DataField, AutoNetworkedField]
    public float LingeringAcidChance = 0.2f;

    [DataField, AutoNetworkedField]
    public float EmpoweredStunSeconds = 1f;

    [DataField, AutoNetworkedField]
    public float EmpoweredImmunitySeconds = 3f;

    /// <summary>
    /// Seconds of delay per tile of distance from the caster. CM13:
    /// addtimer(..., 0.2 SECONDS * get_dist(turf, xeno)).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DistanceDelayPerTileSeconds = 0.2f;

    [DataField, AutoNetworkedField]
    public EntProtoId TelegraphProto = "RMCEffectDespoilerOozingTelegraph";

    [DataField, AutoNetworkedField]
    public EntProtoId AcidSprayProto = "RMCEffectDespoilerAcidSpray";

    [DataField, AutoNetworkedField]
    public EntProtoId AcidSprayEmpoweredProto = "RMCEffectDespoilerAcidSprayEmpowered";

    [DataField, AutoNetworkedField]
    public EntProtoId LingeringAcidProto = "RMCEffectDespoilerLingeringAcid";

    [DataField, AutoNetworkedField]
    public SoundSpecifier? CastSound;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerCatalyzeActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int PlasmaCost;

    [DataField, AutoNetworkedField]
    public int HypertensionCost = 1;

    [DataField, AutoNetworkedField]
    public float BuffDurationSeconds = 10f;

    [DataField, AutoNetworkedField]
    public EntProtoId VisualProto = "RMCEffectDespoilerCatalyze";
}

/// <summary>
/// Active while charging Acid Barrage. Self-slows the Despoiler.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerChargingBarrageComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan StartedAt;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;

    [DataField, AutoNetworkedField]
    public bool Empowered;

    [DataField, AutoNetworkedField]
    public NetCoordinates Target;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 0.5f;
}

/// <summary>
/// Short-lived 3-second acid immunity granted by stepping into an empowered
/// Oozing Wounds spray.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerAcidImmunityComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;
}

/// <summary>
/// Server-side pending list of distance-delayed acid spray spawns scheduled by
/// a single Oozing Wounds cast. Lives on the caster until the wave finishes
/// so two concurrent casts can't bleed into each other.
/// </summary>
[RegisterComponent]
public sealed partial class XenoDespoilerOozingWoundsPendingComponent : Component
{
    public readonly List<XenoDespoilerOozingWoundsPendingTile> Pending = new();
}

public struct XenoDespoilerOozingWoundsPendingTile
{
    public TimeSpan SpawnAt;
    public EntityCoordinates Tile;
    public EntProtoId SprayProto;
    public EntProtoId PuddleProto;
    public bool Empowered;
    public float StunSeconds;
    public float ImmunitySeconds;
    public float PuddleChance;
}
