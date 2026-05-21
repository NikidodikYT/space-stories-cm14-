using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Despoiler;

/// <summary>
/// Marker for the Despoiler caste. Holds the Catalyze empower-next-ability flag.
/// All Despoiler systems gate their behavior on this so base xeno code is never touched.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDespoilerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool NextAbilityEmpowered;

    [DataField, AutoNetworkedField]
    public TimeSpan EmpowerExpiresAt;

    /// <summary>
    /// Currently active Catalyze world visual entity (the bubbles burst parented
    /// to the Despoiler). Tracked so we can despawn it the moment the empower
    /// flag is consumed by an ability — rather than letting it linger for the
    /// full 10-second TimedDespawn. Server-only state; never networked.
    /// </summary>
    [ViewVariables]
    public EntityUid? CatalyzeVisual;

    /// <summary>
    /// Tracks the game time of the last tail stab event to detect hits in MeleeHitEvent.
    /// </summary>
    [ViewVariables]
    public TimeSpan? LastTailStabTime;
}
