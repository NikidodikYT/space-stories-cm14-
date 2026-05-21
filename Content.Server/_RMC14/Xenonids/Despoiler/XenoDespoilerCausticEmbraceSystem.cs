using System.Numerics;
using Content.Shared._RMC14.Actions;
using Content.Shared._RMC14.Map;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Plasma;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Caustic Embrace.
///
/// Normal mode:
///   1. Validate that the snapped landing tile isn't blocked by a wall and is
///      reachable (LOS) — if not, abort BEFORE consuming the empower flag.
///   2. Pounce strictly 1 tile in the cardinal/intercardinal direction nearest
///      the click vector. Click distance only sets the angle.
///   3. Drop the U-shape AoE anchored to the LANDING tile, opening behind the
///      despoiler: telegraph + damage on every non-xeno mob on the U cells +
///      <see cref="XenoDespoilerCausticEmbraceActionComponent.LingeringAcidChance"/>
///      puddle roll per cell.
///
/// Empowered mode:
///   1. Validate that there's a marine target within EmpoweredRange and that
///      we have LOS to it.
///   2. Only THEN consume the empower flag (no more "wasted empower on
///      a misclick").
///   3. Lunge onto the target tile, apply max-level Lingering Acid
///      (DoT + armor debuff) and a brief weaken.
/// </summary>
public sealed class XenoDespoilerCausticEmbraceSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly RMCMapSystem _rmcMap = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedRMCActionsSystem _rmcActions = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly XenoDespoilerCatalyzeFlagSystem _catalyze = default!;
    [Dependency] private readonly XenoDespoilerAcidSystem _acid = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerComponent, XenoDespoilerCausticEmbraceActionEvent>(OnUse);
    }

    private void OnUse(EntityUid uid, XenoDespoilerComponent comp, XenoDespoilerCausticEmbraceActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<XenoDespoilerCausticEmbraceActionComponent>(args.Action, out var action))
            return;

        var ownerXform = Transform(uid);
        var approach = args.Target.Position - ownerXform.Coordinates.Position;
        var dist = approach.Length();
        if (dist < 0.01f)
            return;

        var direction = approach / dist;
        var step = SnapDirectionToTile(direction);
        if (step == Vector2.Zero)
            return;

        // Empower is checked-but-not-consumed here. We commit-or-refund based
        // on per-mode validation below so misclicks don't burn the buff.
        var hasEmpower = _catalyze.IsEmpowered(uid, comp);

        if (hasEmpower)
        {
            if (TryEmpoweredLunge(uid, action, args, ownerXform.Coordinates, dist))
            {
                // Commit plasma + cooldown only after the lunge actually
                // landed. Misclick during empowered mode leaves the buff and
                // plasma untouched.
                if (!_rmcActions.TryUseAction(args))
                    return;
                _catalyze.TakeEmpowerment(uid, comp); // commit only on success
                args.Handled = true;
            }
            return;
        }

        var landing = ownerXform.Coordinates.Offset(step);

        // Wall / impassable check: never teleport through walls.
        if (_rmcMap.IsTileBlocked(landing) ||
            !_interaction.InRangeUnobstructed(uid, landing, range: 2f))
        {
            _popup.PopupEntity(Loc.GetString("rmc-despoiler-pounce-blocked"), uid, uid);
            return;
        }

        if (!_rmcActions.TryUseAction(args))
            return;

        _xform.SetCoordinates(uid, landing);

        if (action.PounceSound is { } sound)
            _audio.PlayPvs(sound, uid);

        SpawnUShape(uid, action, landing, step);

        args.Handled = true;
    }

    /// <summary>
    /// Round a normalized direction vector to the nearest of the 8 grid steps.
    /// Returns <see cref="Vector2.Zero"/> if both components round to 0
    /// (caller must check).
    /// </summary>
    private static Vector2 SnapDirectionToTile(Vector2 dir)
    {
        var x = (float) Math.Sign(MathF.Round(dir.X));
        var y = (float) Math.Sign(MathF.Round(dir.Y));
        return new Vector2(x, y);
    }

    /// <summary>
    /// Spawn the green telegraph + apply burn/acid to mobs + roll puddle on
    /// the seven neighbours of <paramref name="center"/> that AREN'T directly
    /// opposite of <paramref name="forward"/>. <paramref name="forward"/> must
    /// already be snapped to a cardinal/intercardinal step.
    /// </summary>
    private void SpawnUShape(EntityUid caster,
        XenoDespoilerCausticEmbraceActionComponent action,
        EntityCoordinates center,
        Vector2 forward)
    {
        var backX = -(int) forward.X;
        var backY = -(int) forward.Y;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                if (dx == backX && dy == backY)
                    continue;

                var tile = center.Offset(new Vector2(dx, dy)).SnapToGrid(EntityManager);

                var telegraph = Spawn(action.TelegraphProto, tile);
                _hive.SetSameHive(caster, telegraph);

                DamageMobsAt(caster, tile, action);

                if (_random.Prob(action.LingeringAcidChance))
                {
                    var puddle = Spawn(action.LingeringAcidProto, tile);
                    _hive.SetSameHive(caster, puddle);
                    if (TryComp<XenoDespoilerLingeringAcidComponent>(puddle, out var puddleComp))
                    {
                        puddleComp.Owner = caster;
                        Dirty(puddle, puddleComp);
                    }
                }
            }
        }
    }

    private void DamageMobsAt(EntityUid caster, EntityCoordinates tile, XenoDespoilerCausticEmbraceActionComponent action)
    {
        var tileMap = _xform.ToMapCoordinates(tile);
        foreach (var ent in _lookup.GetEntitiesIntersecting(tileMap.MapId,
                     Box2.CenteredAround(tileMap.Position, new Vector2(0.9f, 0.9f))))
        {
            if (!XenoDespoilerVictims.IsValidVictim(EntityManager, ent, caster))
                continue;

            _damageable.TryChangeDamage(ent, action.SplashDamage, ignoreResistances: false, origin: caster);
            _acid.ApplyAcid(ent, caster, action.SplashAcidDurationSeconds);
        }
    }

    /// <summary>
    /// Returns true when the empowered lunge committed (caller commits the
    /// empower flag). Returns false on any validation failure so the caller
    /// can leave the buff intact.
    /// </summary>
    private bool TryEmpoweredLunge(EntityUid uid,
        XenoDespoilerCausticEmbraceActionComponent action,
        XenoDespoilerCausticEmbraceActionEvent args,
        EntityCoordinates origin,
        float dist)
    {
        if (dist > action.EmpoweredRange)
        {
            _popup.PopupEntity(Loc.GetString("rmc-despoiler-pounce-out-of-range"), uid, uid);
            return false;
        }

        var victim = FindEmpoweredVictim(uid, args);
        if (victim is null)
        {
            _popup.PopupEntity(Loc.GetString("rmc-despoiler-caustic-no-target"), uid, uid);
            return false;
        }

        if (!_interaction.InRangeUnobstructed(uid, victim.Value, range: action.EmpoweredRange + 1))
        {
            _popup.PopupEntity(Loc.GetString("rmc-despoiler-pounce-blocked"), uid, uid);
            return false;
        }

        _xform.SetCoordinates(uid, Transform(victim.Value).Coordinates);

        if (action.PounceSound is { } sound)
            _audio.PlayPvs(sound, uid);

        _damageable.TryChangeDamage(victim.Value, action.EmpoweredDamage, ignoreResistances: false, origin: uid);
        _acid.SuperEnhanceAcid(victim.Value);

        // Apply yellow (combo) acid on top of the lunge, matching how
        // RMCXenoRunnerAcider's XenoAcidSlash brands its target. Registry is
        // configured in yaml so designers can tweak duration/damage without
        // touching code.
        if (action.EmpoweredAcid is { } acid)
            EntityManager.AddComponents(victim.Value, acid);

        _stun.TryParalyze(victim.Value, TimeSpan.FromSeconds(action.EmpoweredWeakenSeconds), true);

        return true;
    }

    private EntityUid? FindEmpoweredVictim(EntityUid caster, XenoDespoilerCausticEmbraceActionEvent args)
    {
        // Prefer the entity the player explicitly clicked.
        if (args.Entity is { } target && XenoDespoilerVictims.IsValidVictim(EntityManager, target, caster))
            return target;

        // Fall back to the first valid mob on the target tile.
        var landingMap = _xform.ToMapCoordinates(args.Target);
        foreach (var ent in _lookup.GetEntitiesIntersecting(landingMap.MapId,
                     Box2.CenteredAround(landingMap.Position, new Vector2(1f, 1f))))
        {
            if (XenoDespoilerVictims.IsValidVictim(EntityManager, ent, caster))
                return ent;
        }

        return null;
    }

}
