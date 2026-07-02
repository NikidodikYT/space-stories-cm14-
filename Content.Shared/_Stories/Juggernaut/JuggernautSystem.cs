using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Slow;
using Content.Shared._RMC14.Weapons.Ranged.Overheat;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Stories.Juggernaut;

public sealed class JuggernautSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;
    [Dependency] private readonly RMCSlowSystem _slow = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan BrokenPopupCooldown = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        SubscribeLocalEvent<JuggernautGunComponent, OverheatedEvent>(OnGunOverheated, after: [typeof(OverheatSystem)]);
        SubscribeLocalEvent<JuggernautGunComponent, AttemptShootEvent>(OnGunAttemptShoot);
        SubscribeLocalEvent<JuggernautGunComponent, ItemSlotInsertAttemptEvent>(OnGunInsertAttempt);
        SubscribeLocalEvent<JuggernautGunComponent, JuggernautReloadDoAfterEvent>(OnGunReloadDoAfter);
        SubscribeLocalEvent<JuggernautGunComponent, ExaminedEvent>(OnGunExamined);

        SubscribeLocalEvent<JuggernautArmorComponent, GotEquippedEvent>(OnArmorGotEquipped);
        SubscribeLocalEvent<JuggernautArmorComponent, GotUnequippedEvent>(OnArmorGotUnequipped);

        SubscribeLocalEvent<JuggernautWearerComponent, StoodEvent>(OnWearerStood);
    }

    private void OnGunOverheated(Entity<JuggernautGunComponent> ent, ref OverheatedEvent args)
    {
        if (!args.OverHeated || args.Damage == null)
            return;

        _damageable.TryChangeDamage(ent.Owner, args.Damage, true);
    }

    private void OnGunAttemptShoot(Entity<JuggernautGunComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled || !IsBroken(ent))
            return;

        args.Cancelled = true;

        var time = _timing.CurTime;
        if (time < ent.Comp.LastBrokenPopupAt + BrokenPopupCooldown)
            return;

        ent.Comp.LastBrokenPopupAt = time;
        _popup.PopupClient(Loc.GetString("stories-juggernaut-gun-broken", ("gun", ent.Owner)), args.User, args.User, PopupType.MediumCaution);
    }

    private void OnGunInsertAttempt(Entity<JuggernautGunComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.Inserting)
            return;

        if (args.Slot.ID != ent.Comp.MagazineSlotId)
            return;

        if (args.User is not { } user)
            return;

        args.Cancelled = true;

        var delay = HasAssistant(ent, user) ? ent.Comp.AssistedReloadDelay : ent.Comp.ReloadDelay;
        var doAfter = new DoAfterArgs(EntityManager, user, delay, new JuggernautReloadDoAfterEvent(), ent, ent, args.Item)
        {
            NeedHand = true,
            BreakOnMove = true,
            BlockDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupClient(Loc.GetString("stories-juggernaut-reload-start", ("gun", ent.Owner)), user, user);
    }

    private void OnGunReloadDoAfter(Entity<JuggernautGunComponent> ent, ref JuggernautReloadDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (args.Used is not { } belt)
            return;

        ent.Comp.Inserting = true;
        try
        {
            _itemSlots.TryInsert(ent, ent.Comp.MagazineSlotId, belt, args.User);
        }
        finally
        {
            ent.Comp.Inserting = false;
        }
    }

    private void OnGunExamined(Entity<JuggernautGunComponent> ent, ref ExaminedEvent args)
    {
        if (IsBroken(ent))
            args.PushMarkup(Loc.GetString("stories-juggernaut-examine-broken", ("gun", ent.Owner)));
    }

    private void OnArmorGotEquipped(Entity<JuggernautArmorComponent> ent, ref GotEquippedEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if ((ent.Comp.Slots & args.SlotFlags) == 0)
            return;

        var wearer = EnsureComp<JuggernautWearerComponent>(args.Equipee);
        wearer.StandUpSlowdown = ent.Comp.StandUpSlowdown;
        Dirty(args.Equipee, wearer);
    }

    private void OnArmorGotUnequipped(Entity<JuggernautArmorComponent> ent, ref GotUnequippedEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if ((ent.Comp.Slots & args.SlotFlags) == 0)
            return;

        RemCompDeferred<JuggernautWearerComponent>(args.Equipee);
    }

    private void OnWearerStood(Entity<JuggernautWearerComponent> ent, ref StoodEvent args)
    {
        _slow.TrySlowdown(ent, ent.Comp.StandUpSlowdown);
    }

    private bool IsBroken(Entity<JuggernautGunComponent> gun)
    {
        return TryComp(gun, out DamageableComponent? damageable) &&
               damageable.TotalDamage >= gun.Comp.BrokenThreshold;
    }

    private bool HasAssistant(Entity<JuggernautGunComponent> gun, EntityUid user)
    {
        var nearby = _lookup.GetEntitiesInRange<SkillsComponent>(Transform(user).Coordinates, gun.Comp.AssistRange);
        foreach (var marine in nearby)
        {
            if (marine.Owner == user || !_mobState.IsAlive(marine))
                continue;

            if (_skills.HasSkill(marine.Owner, gun.Comp.AssistSkill, gun.Comp.AssistSkillLevel))
                return true;
        }

        return false;
    }
}

[Serializable, NetSerializable]
public sealed partial class JuggernautReloadDoAfterEvent : SimpleDoAfterEvent;
