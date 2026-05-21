using System.Numerics;
using Content.Shared._RMC14.Actions;
using Content.Shared._RMC14.Projectiles;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Plasma;
using Content.Shared._RMC14.Xenonids.Despoiler;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Xenonids.Despoiler;

/// <summary>
/// Acid Barrage state machine.
///
///   Action button (WorldTargetAction) → cursor enters target-select mode.
///     The player's LMB click on a tile is what actually starts the cast
///     (plasma cost + self-slow + charge timer); that tile is locked in as
///     <c>charging.Target</c>.
///   RMB during charge → cancel without firing
///     (<see cref="OnCancelRequest"/>, refunds nothing, only the cancel
///     cooldown applies).
///   LMB during charge → fire the volley early, re-aiming at the new cursor
///     position. <see cref="OnFireRequest"/> re-checks ActionBlocker so
///     muted/cuffed/stunned operators can't unleash a barrage by clicking
///     through the charge.
///   Max-charge timeout → auto-fire at the locked target.
///
/// Per-projectile:
///   * scatter ±ScatterDegrees
///   * random travel distance MinRangeTiles..MaxRangeTiles (CM13 rand(1,6))
///   * random scale MinProjectileScale..MaxProjectileScale (CM13 rand(0.9,1.33))
///   * ProjectileMaxRangeComponent auto-deletes the shot at its target tile,
///     where the projectile's EntityTerminating hook drops a lingering acid
///     puddle.
/// </summary>
public sealed class XenoDespoilerAcidBarrageSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly RMCProjectileSystem _rmcProjectile = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedRMCActionsSystem _rmcActions = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly XenoDespoilerCatalyzeFlagSystem _catalyze = default!;
    [Dependency] private readonly XenoPlasmaSystem _plasma = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDespoilerComponent, XenoDespoilerAcidBarrageActionEvent>(OnAction);
        SubscribeLocalEvent<XenoDespoilerAcidBarrageActionComponent, RMCActionUseAttemptEvent>(OnActionAttempt);
        SubscribeNetworkEvent<XenoDespoilerBarrageFireRequest>(OnFireRequest);
        SubscribeNetworkEvent<XenoDespoilerBarrageCancelRequest>(OnCancelRequest);
    }

    /// <summary>
    /// Intercept the action attempt BEFORE plasma is spent or useDelay is
    /// applied. If the caster is already charging, this press is a cancel —
    /// clean up the charge and refund the cast (no plasma, no full cooldown).
    /// </summary>
    private void OnActionAttempt(Entity<XenoDespoilerAcidBarrageActionComponent> action, ref RMCActionUseAttemptEvent args)
    {
        // We deliberately do NOT short-circuit on args.Cancelled — the cancel
        // path has to work even if another subscriber (e.g. XenoActionPlasma
        // out of plasma) already cancelled, so the player isn't stuck in a
        // charge state they can't get out of.
        if (!HasComp<XenoDespoilerChargingBarrageComponent>(args.User))
            return;

        RemComp<XenoDespoilerChargingBarrageComponent>(args.User);
        // The LifeStage guard in XenoDespoilerBarrageChargeSpeedSystem.OnRefresh
        // already skips applying the modifier during shutdown, so the
        // ComponentShutdown-triggered refresh cleans the slow on its own.

        if (action.Comp.CancelCooldownSeconds > 0)
        {
            _actions.SetCooldown((action.Owner, null), TimeSpan.FromSeconds(action.Comp.CancelCooldownSeconds));
        }

        _popup.PopupEntity(Loc.GetString("rmc-despoiler-barrage-cancelled"), args.User, args.User);
        args.Cancelled = true;
    }

    private void OnAction(EntityUid uid, XenoDespoilerComponent comp, XenoDespoilerAcidBarrageActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<XenoDespoilerAcidBarrageActionComponent>(args.Action, out var action))
            return;

        // The cancel path is handled in OnActionAttempt — by the time we get
        // here the caster cannot already be charging.

        // Commits the action use: deducts plasma via XenoActionPlasma's
        // RMCActionUseEvent handler and starts the action cooldown. Without
        // this call the action proceeds for free.
        if (!_rmcActions.TryUseAction(args))
            return;

        var charging = AddComp<XenoDespoilerChargingBarrageComponent>(uid);
        charging.StartedAt = _timing.CurTime;
        charging.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(action.MaxChargeSeconds);
        charging.Empowered = _catalyze.TakeEmpowerment(uid, comp);
        // Target locked at the moment the player LMB-confirmed the action's
        // target-select cursor. Auto-expire fires at this tile; LMB during
        // charge can re-aim and fire early at a new cursor position.
        charging.Target = GetNetCoordinates(args.Target);
        charging.SpeedMultiplier = action.ChargingSpeedMultiplier;
        Dirty(uid, charging);

        if (action.ChargeSound is { } sound)
            _audio.PlayPvs(sound, uid);

        _popup.PopupEntity(Loc.GetString("rmc-despoiler-barrage-charging"), uid, uid);
        args.Handled = true;
    }

    /// <summary>
    /// Client-initiated fire trigger. Re-validates the caster from scratch —
    /// the client can lie about anything except its session, so every gate the
    /// normal action pipeline applies (ActionBlocker, action cooldown, having
    /// a Despoiler component) is re-checked here.
    /// </summary>
    private void OnFireRequest(XenoDespoilerBarrageFireRequest msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid)
            return;

        if (!HasComp<XenoDespoilerComponent>(uid))
            return;

        if (!TryComp<XenoDespoilerChargingBarrageComponent>(uid, out var charge))
            return;

        // If the despoiler got stunned / cuffed / killed between the start of
        // the charge and this LMB press, drop the charge silently — the
        // slow + HUD shutdown handles cleanup, and no projectiles are spent.
        if (!_actionBlocker.CanConsciouslyPerformAction(uid))
        {
            RemComp<XenoDespoilerChargingBarrageComponent>(uid);
            return;
        }

        if (!TryGetBarrageAction(uid, out _, out var actionComp))
            return;

        // Mouse-aim is mandatory; an invalid click is rejected outright rather
        // than falling back to facing direction.
        var aim = GetCoordinates(msg.Target);
        if (!aim.IsValid(EntityManager))
            return;

        // We deliberately DO NOT check action.Cooldown here: that cooldown
        // started the moment the player pressed the action button to begin
        // charging, and we're firing in the middle of that window. The charge
        // component itself is the gate — once we consume it the next click is
        // a no-op until the next cast cycle.
        FireVolley(uid, actionComp, charge, aim);
        RemComp<XenoDespoilerChargingBarrageComponent>(uid);
    }

    private void OnCancelRequest(XenoDespoilerBarrageCancelRequest msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid)
            return;

        if (!HasComp<XenoDespoilerComponent>(uid))
            return;

        if (!TryComp<XenoDespoilerChargingBarrageComponent>(uid, out _))
            return;

        RemComp<XenoDespoilerChargingBarrageComponent>(uid);

        if (TryGetBarrageAction(uid, out var actionEnt, out var actionComp))
        {
            if (actionComp.CancelCooldownSeconds > 0)
            {
                _actions.SetCooldown((actionEnt.Owner, null), TimeSpan.FromSeconds(actionComp.CancelCooldownSeconds));
            }
        }

        _popup.PopupEntity(Loc.GetString("rmc-despoiler-barrage-cancelled"), uid, uid);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoDespoilerChargingBarrageComponent>();
        while (query.MoveNext(out var uid, out var charge))
        {
            // Stun / cuffs / paralysis / death interrupts the charge: clear
            // the component (slow + HUD bar drop via ComponentShutdown) but do
            // NOT fire a volley and do NOT refund / re-spend plasma.
            if (!_actionBlocker.CanConsciouslyPerformAction(uid))
            {
                RemComp<XenoDespoilerChargingBarrageComponent>(uid);
                _popup.PopupEntity(Loc.GetString("rmc-despoiler-barrage-cancelled"), uid, uid);
                continue;
            }

            if (now < charge.ExpiresAt)
                continue;

            // Charge maxed out — auto-fire at the LMB-locked target picked at
            // cast time.
            if (TryGetBarrageAction(uid, out _, out var actionComp))
                FireVolley(uid, actionComp, charge, GetCoordinates(charge.Target));

            RemComp<XenoDespoilerChargingBarrageComponent>(uid);
        }
    }

    /// <summary>
    /// Canonical action lookup via the RMC actions API. Replaces the
    /// transform-child walk that assumed action entities are always parented
    /// to the user — they're stored in <c>ActionsContainerComponent</c>, not
    /// the transform tree.
    /// </summary>
    private bool TryGetBarrageAction(EntityUid xeno,
        out Entity<ActionComponent> actionEnt,
        out XenoDespoilerAcidBarrageActionComponent action)
    {
        foreach (var entry in _rmcActions.GetActionsWithEvent<XenoDespoilerAcidBarrageActionEvent>(xeno))
        {
            if (!TryComp(entry.Owner, out XenoDespoilerAcidBarrageActionComponent? barrage))
                continue;

            actionEnt = entry;
            action = barrage;
            return true;
        }

        actionEnt = default;
        action = default!;
        return false;
    }

    private void FireVolley(EntityUid uid, XenoDespoilerAcidBarrageActionComponent action,
        XenoDespoilerChargingBarrageComponent charge, EntityCoordinates target)
    {
        var heldFor = (float) (_timing.CurTime - charge.StartedAt).TotalSeconds;
        var chargeFrac = Math.Clamp(heldFor / action.MaxChargeSeconds, 0f, 1f);

        var rolled = (int) MathF.Round(MathHelper.Lerp(action.MinProjectiles, action.MaxProjectiles, chargeFrac));
        var count = Math.Clamp(rolled, action.MinProjectiles, action.MaxProjectiles);
        if (charge.Empowered)
            count += action.EmpowerBonusProjectiles;

        var casterXform = Transform(uid);
        var casterCoords = casterXform.Coordinates;

        // Caller (OnFireRequest) already rejected invalid coords. A click on
        // the caster's own tile still degenerates to zero-length — fall back
        // to facing direction in that single edge case so we don't normalize a
        // zero vector.
        var aimVec = target.Position - casterCoords.Position;
        if (aimVec.LengthSquared() < 0.0001f)
            aimVec = casterXform.LocalRotation.ToWorldVec();

        var baseAngle = MathF.Atan2(aimVec.Y, aimVec.X);
        var aimDir = Vector2.Normalize(aimVec);
        // Spawn one tile in front of the caster so projectiles don't
        // materialise inside the despoiler's hitbox.
        var spawnCoords = casterCoords.Offset(aimDir);
        var scatterRad = MathF.PI / 180f * action.ScatterDegrees;
        var scaleSpan = action.MaxProjectileScale - action.MinProjectileScale;

        for (var i = 0; i < count; i++)
        {
            var offset = ((float) _random.NextDouble() * 2f - 1f) * scatterRad;
            var angle = baseAngle + offset;
            var unit = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var rangeTiles = _random.Next(action.MinRangeTiles, action.MaxRangeTiles + 1);

            var proj = Spawn(action.ProjectileId, spawnCoords);

            // Tag with the firing hive so other xenos won't be friendly-fired
            // by splash / puddle damage downstream.
            _hive.SetSameHive(uid, proj);

            if (TryComp<XenoDespoilerAcidBarrageProjectileComponent>(proj, out var projComp))
            {
                projComp.Shooter = uid;
                projComp.LingeringAcidChance = action.LingeringAcidChance;
                projComp.SplashRadius = action.SplashRadius;
                var scaleFactor = action.MinProjectileScale + (float) _random.NextDouble() * scaleSpan;
                projComp.Scale = new Vector2(scaleFactor, scaleFactor);
                Dirty(proj, projComp);
            }

            // Travel distance is set on the projectile (RMCProjectileSystem
            // auto-deletes at distance reached, then our terminate hook drops
            // the lingering acid).
            _rmcProjectile.SetMaxRange(proj, rangeTiles);

            // ShootProjectile sets BodyStatus.InAir, normalises direction,
            // applies velocity, wires Projectile.Shooter (IgnoreShooter then
            // skips the despoiler) and rotates the sprite to face travel.
            _gun.ShootProjectile(proj, unit * rangeTiles, Vector2.Zero, uid, uid, speed: action.ProjectileSpeed);
        }

        if (action.FireSound is { } sound)
            _audio.PlayPvs(sound, uid);
    }
}
