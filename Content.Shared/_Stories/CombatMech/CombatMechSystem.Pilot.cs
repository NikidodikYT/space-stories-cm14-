using System.Numerics;
using Content.Shared._RMC14.Sprite;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Camera;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction.Components;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared._RMC14.Suicide;
using Content.Shared.Verbs;
using Robust.Shared.Maths;

namespace Content.Shared._Stories.CombatMech;


public sealed partial class CombatMechSystem
{
    private void OnStrapAttempt(Entity<CombatMechComponent> ent, ref StrapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.User is not { } user)
            return;

        if (!_skills.HasSkill(user, ent.Comp.WeaponSkill, ent.Comp.WeaponSkillRequired))
        {
            if (args.Popup)
                _popup.PopupClient(Loc.GetString("stories-rx47-not-trained-pilot"), ent, user, PopupType.MediumCaution);
            args.Cancelled = true;
            return;
        }

        if (GetWeapon(ent, WeaponSlot.Primary) == null || GetWeapon(ent, WeaponSlot.Secondary) == null)
        {
            if (args.Popup)
                _popup.PopupClient(Loc.GetString("stories-rx47-missing-weapons"), ent, user, PopupType.MediumCaution);
            args.Cancelled = true;
            return;
        }

        if (!TryComp(user, out HandsComponent? hands) || _hands.CountFreeHands((user, hands)) < 2)
        {
            if (args.Popup)
                _popup.PopupClient(Loc.GetString("stories-rx47-need-both-hands"), ent, user, PopupType.MediumCaution);
            args.Cancelled = true;
        }
    }

    private void OnUnstrapAttempt(Entity<CombatMechComponent> ent, ref UnstrapAttemptEvent args)
    {
        if (args.Cancelled ||
            !ent.Comp.HelmetClosed ||
            _forceEjectingPilots.Contains(args.Buckle.Owner))
        {
            return;
        }

        if (args.Popup)
            _popup.PopupClient(Loc.GetString("stories-rx47-faceplate-blocks-exit"), ent, args.User, PopupType.MediumCaution);

        args.Cancelled = true;
    }

    private void OnStrapped(Entity<CombatMechComponent> ent, ref StrappedEvent args)
    {
        var pilot = args.Buckle.Owner;

        ent.Comp.PilotEntity = pilot;
        DirtyField(ent.Owner, ent.Comp, nameof(CombatMechComponent.PilotEntity));

        var inside = EnsureComp<InsideCombatVehicleComponent>(pilot);
        inside.Vehicle = ent;
        DirtyField(pilot, inside, nameof(InsideCombatVehicleComponent.Vehicle));
        if (_net.IsServer)
            _pilotsInCombatMechs.Add(pilot);
        UpdatePilotProtection((pilot, inside));
        ApplyPilotVisuals((pilot, inside));

        _mover.SetRelay(pilot, ent);
        var relay = EnsureComp<InteractionRelayComponent>(pilot);
        _interaction.SetRelay(pilot, ent, relay);

        _audio.PlayPredicted(ent.Comp.EnterSound, ent, pilot);
        _movementSpeed.RefreshMovementSpeedModifiers(ent);

        if (_net.IsServer)
        {
            _rmcPulling.TryStopAllPullsFromAndOn(pilot);

            if (!TransferWeaponToPilot(ent, pilot, WeaponSlot.Primary) ||
                !TransferWeaponToPilot(ent, pilot, WeaponSlot.Secondary))
            {
                EjectPilotAfterWeaponTransferFailure(ent, pilot);
                return;
            }
        }

        UpdateAppearance(ent);
    }

    private void OnUnstrapped(Entity<CombatMechComponent> ent, ref UnstrappedEvent args)
    {
        var pilot = args.Buckle.Owner;

        if (_net.IsServer)
        {
            TransferWeaponToMech(ent, pilot, WeaponSlot.Primary);
            TransferWeaponToMech(ent, pilot, WeaponSlot.Secondary);
        }

        RemCompDeferred<InsideCombatVehicleComponent>(pilot);

        _audio.PlayPredicted(ent.Comp.ExitSound, ent, pilot);
    }

    private bool TransferWeaponToPilot(Entity<CombatMechComponent> ent, EntityUid pilot, WeaponSlot slot)
    {
        if (GetWeapon(ent, slot) is not { } weapon)
            return false;

        if (!TryComp(pilot, out HandsComponent? pilotHands))
            return false;

        var hand = FindHand(pilot, pilotHands, GetHandLocationFor(slot));
        if (hand == null)
            return false;

        LinkWeaponToMech(weapon, ent);

        // Only toggle UnremoveableComponent if we actually need to drop the weapon out of mech's hand.
        if (_hands.IsHolding(ent.Owner, weapon))
        {
            RemComp<UnremoveableComponent>(weapon);
            var dropped = _hands.TryDrop(ent.Owner, weapon, Transform(ent).Coordinates, checkActionBlocker: false, doDropInteraction: false);
            EnsureWeaponUnremoveable(weapon);
            if (!dropped)
                return false;
        }

        if (!_hands.TryPickup(pilot, weapon, hand, checkActionBlocker: false, animate: false, handsComp: pilotHands))
        {
            TransferWeaponToMech(ent, pilot, slot);
            return false;
        }

        EnsureWeaponUnremoveable(weapon);
        return true;
    }

    private void TransferWeaponToMech(Entity<CombatMechComponent> ent, EntityUid pilot, WeaponSlot slot)
    {
        if (GetWeapon(ent, slot) is not { } weapon)
            return;

        if (!TryComp(ent.Owner, out HandsComponent? mechHands))
            return;

        var hand = FindHand(ent.Owner, mechHands, GetHandLocationFor(slot));
        if (hand == null)
            return;

        LinkWeaponToMech(weapon, ent);

        if (_hands.IsHolding(ent.Owner, weapon))
        {
            EnsureWeaponUnremoveable(weapon);
            return;
        }

        // Only toggle UnremoveableComponent if we actually need to drop the weapon out of pilot's hand.
        if (_hands.IsHolding(pilot, weapon))
        {
            RemComp<UnremoveableComponent>(weapon);
            var dropped = _hands.TryDrop(pilot, weapon, Transform(ent).Coordinates, checkActionBlocker: false, doDropInteraction: false);
            EnsureWeaponUnremoveable(weapon);
            if (!dropped)
                return;
        }

        if (!_hands.TryPickup(ent.Owner, weapon, hand, checkActionBlocker: false, animate: false, handsComp: mechHands))
        {
            RemComp<UnremoveableComponent>(weapon);
            if (TryComp(weapon, out CombatMechWeaponComponent? weaponComp))
                ClearWeaponMechLink((weapon, weaponComp));
            SetWeapon(ent, slot, null);
            _transform.SetCoordinates(weapon, Transform(ent).Coordinates);
            Log.Warning($"RX47 failed to return {ToPrettyString(weapon)} to {ToPrettyString(ent.Owner)} hand {hand} ({slot}).");
            return;
        }

        EnsureWeaponUnremoveable(weapon);
    }

    private void EjectPilotAfterWeaponTransferFailure(Entity<CombatMechComponent> mech, EntityUid pilot)
    {
        Log.Warning($"RX47 ejected {ToPrettyString(pilot)} from {ToPrettyString(mech.Owner)} after weapon transfer failed.");

        if (TryComp(pilot, out BuckleComponent? buckle))
        {
            if (_buckle.TryUnbuckle(pilot, pilot, buckle, popup: false))
                return;

            // TryUnbuckle was blocked by an event; force-release through the no-check Unbuckle.
            // It still raises UnstrappedEvent so OnUnstrapped runs the normal weapon-return path.
            _buckle.Unbuckle((pilot, buckle), null);
        }

        // Last-resort cleanup if buckle is missing or Unbuckle did not propagate to OnUnstrapped.
        if (mech.Comp.PilotEntity == pilot)
        {
            if (_net.IsServer)
            {
                TransferWeaponToMech(mech, pilot, WeaponSlot.Primary);
                TransferWeaponToMech(mech, pilot, WeaponSlot.Secondary);
            }

            RemCompDeferred<InsideCombatVehicleComponent>(pilot);
        }
    }

    private void OnInsideVehicleMove(Entity<InsideCombatVehicleComponent> ent, ref MoveEvent args)
    {
        UpdatePilotVisualOffset(ent);
    }

    private void OnInsideVehicleState(Entity<InsideCombatVehicleComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyPilotVisuals(ent);
    }

    private void OnInsideVehicleShutdown(Entity<InsideCombatVehicleComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsServer)
            _pilotsInCombatMechs.Remove(ent.Owner);

        RestorePilotProtection(ent);
        RestorePilotVisuals(ent);
        RemComp<RelayInputMoverComponent>(ent.Owner);
        RemCompDeferred<InteractionRelayComponent>(ent.Owner);

        if (!Deleted(ent.Comp.Vehicle))
        {
            RemComp<MovementRelayTargetComponent>(ent.Comp.Vehicle);
            if (TryComp(ent.Comp.Vehicle, out CombatMechComponent? mech) && mech.PilotEntity == ent.Owner)
            {
                mech.PilotEntity = null;
                DirtyField(ent.Comp.Vehicle, mech, nameof(CombatMechComponent.PilotEntity));
                _movementSpeed.RefreshMovementSpeedModifiers(ent.Comp.Vehicle);
                UpdateAppearance((ent.Comp.Vehicle, mech));
            }
        }
    }

    private void OnInsideVehicleGetEyeOffsetAttempt(Entity<InsideCombatVehicleComponent> ent, ref GetEyeOffsetAttemptEvent args)
    {
        // Keep the pilot camera anchored to the mech instead of applying the mob's own visual offset.
        args.Cancelled = true;
    }

    // Cancels the suicide doafter at its source instead of trying to scrape the verb out of the menu
    // by matching a localized string. Survives Loc-key renames and verb-text suffix changes upstream.
    // Subscribed `before` RMCSuicideSystem so setting Handled=true short-circuits its OnSuicideDoAfter.
    private void OnInsideVehicleSuicideAttempt(Entity<InsideCombatVehicleComponent> ent, ref RMCSuicideDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!HasLiveVehicle(ent))
            return;

        args.Handled = true;
    }

    private void ApplyPilotVisuals(Entity<InsideCombatVehicleComponent> pilot)
    {
        if (!HasLiveVehicle(pilot) ||
            !TryComp(pilot.Comp.Vehicle, out CombatMechComponent? mech))
        {
            return;
        }

        ApplyPilotVisuals(pilot.Owner, (pilot.Comp.Vehicle, mech));
    }

    private void ApplyPilotVisuals(EntityUid pilot, Entity<CombatMechComponent> mech)
    {
        UpdatePilotVisualOffset(pilot, mech);
        SetPilotRenderOrder(pilot, mech.Comp.PilotRenderOrder);
        _rmcSprite.UpdateDrawDepth(pilot);
    }

    private void UpdatePilotVisualOffset(Entity<InsideCombatVehicleComponent> pilot)
    {
        if (!HasLiveVehicle(pilot) ||
            !TryComp(pilot.Comp.Vehicle, out CombatMechComponent? mech))
        {
            return;
        }

        UpdatePilotVisualOffset(pilot.Owner, (pilot.Comp.Vehicle, mech));
    }

    private void UpdatePilotVisualOffset(EntityUid pilot, Entity<CombatMechComponent> mech)
    {
        var direction = Transform(pilot).LocalRotation.GetCardinalDir();
        var offset = direction switch
        {
            Direction.North => mech.Comp.PilotVisualOffsetNorth,
            Direction.South => mech.Comp.PilotVisualOffsetSouth,
            Direction.East or Direction.West => mech.Comp.PilotVisualOffsetEastWest,
            _ => mech.Comp.PilotVisualOffsetEastWest,
        };

        SetPilotVisualOffset(pilot, offset);
    }

    private void RestorePilotVisuals(Entity<InsideCombatVehicleComponent> pilot)
    {
        RestorePilotDefaultVisuals(pilot.Owner);
    }

    // Zero the SpriteSetRenderOrder fields rather than removing the component:
    // the visualizer FrameUpdate keeps applying these (so removal would skip the
    // last reset), and removal/re-add on every strap cycle causes the bouncing
    // pattern that previously broke render order on re-entry.
    private void RestorePilotDefaultVisuals(EntityUid pilot)
    {
        if (HasComp<SpriteSetRenderOrderComponent>(pilot))
        {
            _rmcSprite.SetOffset(pilot, Vector2.Zero);
            _rmcSprite.SetRenderOrder(pilot, 0);
        }

        _rmcSprite.UpdateDrawDepth(pilot);
    }

    private void SetPilotVisualOffset(EntityUid pilot, Vector2 offset)
    {
        _rmcSprite.SetOffset(pilot, offset);
    }

    private void SetPilotRenderOrder(EntityUid pilot, int renderOrder)
    {
        _rmcSprite.SetRenderOrder(pilot, renderOrder);
    }
}
