using System.Numerics;
using Content.Shared._RMC14.Actions;
using Content.Shared._RMC14.Map;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._RMC14.Xenonids.Despoiler;

public sealed class XenoDespoilerCausticEmbraceSystem : EntitySystem
{
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

    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<XenoComponent> _xenoQuery;
    private EntityQuery<XenoDespoilerLingeringAcidComponent> _lingeringQuery;

    public override void Initialize()
    {
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _xenoQuery = GetEntityQuery<XenoComponent>();
        _lingeringQuery = GetEntityQuery<XenoDespoilerLingeringAcidComponent>();

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

        var step = SnapDirectionToTile(approach / dist);
        if (step == Vector2.Zero)
            return;

        if (_catalyze.IsEmpowered(uid, comp))
        {
            if (TryEmpoweredLunge(uid, action, args, dist))
            {
                if (!_rmcActions.TryUseAction(args))
                    return;
                _catalyze.TakeEmpowerment(uid, comp);
                args.Handled = true;
            }
            return;
        }

        var landing = ownerXform.Coordinates.Offset(step);

        if (_rmcMap.IsTileBlocked(landing) ||
            !_interaction.InRangeUnobstructed(uid, landing, range: action.NormalRange + 1f))
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

    private static Vector2 SnapDirectionToTile(Vector2 dir)
    {
        return new Vector2(Math.Sign(MathF.Round(dir.X)), Math.Sign(MathF.Round(dir.Y)));
    }

    private void SpawnUShape(EntityUid caster,
        XenoDespoilerCausticEmbraceActionComponent action,
        EntityCoordinates center,
        Vector2 forward)
    {
        var backX = -(int)forward.X;
        var backY = -(int)forward.Y;

        // Single lookup at the U-shape center, filter by tile inline — avoids 8 physics calls.
        var centerMap = _xform.ToMapCoordinates(center);
        var hits = _lookup.GetEntitiesIntersecting(centerMap.MapId,
            Box2.CenteredAround(centerMap.Position, new Vector2(action.SplashScanDiameter, action.SplashScanDiameter)));

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                if (dx == backX && dy == backY)
                    continue;

                var tile = center.Offset(new Vector2(dx, dy));
                var tileMap = _xform.ToMapCoordinates(tile);

                var telegraph = Spawn(action.TelegraphProto, tile);
                _hive.SetSameHive(caster, telegraph);

                foreach (var ent in hits)
                {
                    if (!XenoDespoilerVictims.IsValidVictim(EntityManager, ent, caster))
                        continue;

                    var entPos = _xform.ToMapCoordinates(Transform(ent).Coordinates).Position;
                    if (Math.Abs(entPos.X - tileMap.Position.X) > 0.5f) continue;
                    if (Math.Abs(entPos.Y - tileMap.Position.Y) > 0.5f) continue;

                    _damageable.TryChangeDamage(ent, action.SplashDamage, ignoreResistances: false, origin: caster);
                    _acid.ApplyAcid(ent, caster, action.SplashAcidDuration);
                }

                if (_random.Prob(action.LingeringAcidChance))
                {
                    var puddle = Spawn(action.LingeringAcidProto, tile);
                    _hive.SetSameHive(caster, puddle);
                    if (_lingeringQuery.TryComp(puddle, out var puddleComp))
                    {
                        puddleComp.Caster = caster;
                        Dirty(puddle, puddleComp);
                    }
                }
            }
        }
    }

    private bool TryEmpoweredLunge(EntityUid uid,
        XenoDespoilerCausticEmbraceActionComponent action,
        XenoDespoilerCausticEmbraceActionEvent args,
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
        _acid.ApplyAcid(victim.Value, uid, action.SplashAcidDuration, enhance: true);

        _stun.TryParalyze(victim.Value, action.EmpoweredWeakenDuration, true);

        return true;
    }

    private EntityUid? FindEmpoweredVictim(EntityUid caster, XenoDespoilerCausticEmbraceActionEvent args)
    {
        if (args.Entity is { } target && XenoDespoilerVictims.IsValidVictim(EntityManager, target, caster))
            return target;

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
