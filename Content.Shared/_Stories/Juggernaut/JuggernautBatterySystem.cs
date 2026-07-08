using Content.Shared.PowerCell.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Shared._Stories.Juggernaut;

public sealed class JuggernautBatterySystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<JuggernautBatteryComponent, ContainerGettingInsertedAttemptEvent>(OnBatteryInsertedAttempt);
    }

    private void OnBatteryInsertedAttempt(Entity<JuggernautBatteryComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var container = args.Container;
        if (TryComp(container.Owner, out PowerCellSlotComponent? slot) &&
            container.ID == slot.CellSlotId &&
            _whitelist.IsWhitelistFail(ent.Comp.Guns, container.Owner))
        {
            args.Cancel();
        }
    }
}
