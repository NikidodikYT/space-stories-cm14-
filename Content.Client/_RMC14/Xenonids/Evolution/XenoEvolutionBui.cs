using System.Linq;
using Content.Client._RMC14.Xenonids.UI;
using Content.Client.Message;
using Content.Shared._RMC14.Xenonids.Evolution;
using Content.Shared._RMC14.Xenonids.Strain;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._RMC14.Xenonids.Evolution;

[UsedImplicitly]
public sealed class XenoEvolutionBui : BoundUserInterface
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly SpriteSystem _sprite;

    [ViewVariables]
    private XenoEvolutionWindow? _window;

    private readonly Dictionary<EntProtoId, XenoChoiceControl> _evolutionControls = new();
    private readonly Dictionary<EntProtoId, XenoChoiceControl> _strainControls = new();
    private readonly Dictionary<EntProtoId, XenoChoiceControl> _lotteryControls = new();

    public XenoEvolutionBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _sprite = EntMan.System<SpriteSystem>();
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<XenoEvolutionWindow>();
        _window.OvipositorNeededLabel.Visible = false;

        if (EntMan.TryGetComponent(Owner, out XenoEvolutionComponent? xeno))
        {
            foreach (var strain in xeno.Strains)
            {
                AddStrain(strain);
            }
        }

        _window.StrainsLabel.Visible = _window.StrainsContainer.ChildCount > 0;
        Refresh();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        Refresh();
    }

    private void AddEvolution(EntProtoId evolutionId)
    {
        if (!_prototype.TryIndex(evolutionId, out var evolution))
            return;

        if (!_evolutionControls.TryGetValue(evolutionId, out var control))
        {
            control = new XenoChoiceControl();
            control.Set(evolution.Name, _sprite.Frame0(evolution));
            control.Button.Disabled = false;

            control.Button.OnPressed += _ =>
            {
                SendPredictedMessage(new XenoEvolveBuiMsg(evolutionId));
                Close();
            };

            _evolutionControls[evolutionId] = control;
            _window?.EvolutionsContainer.AddChild(control);
        }

        control.Visible = true;
        control.Button.Disabled = false;
    }

    private void AddStrain(EntProtoId strainId)
    {
        if (_window is not { IsOpen: true })
            return;

        if (!_prototype.TryIndex(strainId, out var strain))
            return;

        if (!_strainControls.TryGetValue(strainId, out var control))
        {
            control = new XenoChoiceControl();

            var name = strain.Name;
            string? description = null;

            if (strain.TryGetComponent(out XenoStrainComponent? strainComp))
            {
                name = $"{Loc.GetString(strainComp.Name)} {name}";
                description = strainComp.Description;
            }

            control.Set(name, _sprite.Frame0(strain));
            control.Button.Disabled = false;

            control.Button.OnPressed += _ =>
            {
                var confirmWindow = new XenoStrainConfirmWindow();
                confirmWindow.SetInfo(name, _sprite.Frame0(strain), description);

                confirmWindow.OnConfirm += () =>
                {
                    SendPredictedMessage(new XenoStrainBuiMsg(strainId));
                    confirmWindow.Close();
                    Close();
                };

                confirmWindow.OpenCentered();
            };

            _strainControls[strainId] = control;
            _window.StrainsContainer.AddChild(control);
        }

        control.Visible = true;
        control.Button.Disabled = false;
    }

    private void AddLottery(EntProtoId lotteryId)
    {
        if (!_prototype.TryIndex(lotteryId, out var caste))
            return;

        if (!_lotteryControls.TryGetValue(lotteryId, out var control))
        {
            control = new XenoChoiceControl();
            control.Set(caste.Name, _sprite.Frame0(caste));
            control.Button.ToggleMode = true;

            control.Button.OnPressed += _ =>
            {
                SendPredictedMessage(new XenoLotteryRegisterBuiMsg(lotteryId));
            };

            _lotteryControls[lotteryId] = control;
            _window?.LotteryContainer.AddChild(control);
        }

        control.Visible = true;
    }

    public void Refresh()
    {
        if (_window == null)
            return;

        if (!EntMan.TryGetComponent(Owner, out XenoEvolutionComponent? xeno))
            return;

        var state = State as XenoEvolveBuiState;
        // Stories-start: only offer the lottery once the xeno can afford the evolution, mirroring the
        // normal evolve buttons which are gated on points below.
        var lotteryChoices = xeno.Points >= xeno.Max ? state?.LotteryChoices : null;
        // Stories-end

        _window.PointsLabel.Visible = xeno.Max > FixedPoint2.Zero;

        foreach (var control in _evolutionControls.Values)
            control.Visible = false;

        foreach (var evolutionId in xeno.EvolvesToWithoutPoints)
            AddEvolution(evolutionId);

        if (xeno.Points >= xeno.Max)
        {
            foreach (var evolutionId in xeno.EvolvesTo)
            {
                // Castes still being raffled appear only as lottery toggles below, not as normal buttons.
                if (lotteryChoices != null && lotteryChoices.Contains(evolutionId))
                    continue;

                AddEvolution(evolutionId);
            }
        }

        _window.Separator.Visible = _window.EvolutionsContainer.Children.Any(child => child.Visible) &&
                                    _window.StrainsContainer.Children.Any(child => child.Visible);

        var lackingOvipositor = state is { LackingOvipositor: true };
        var points = xeno.Points;

        _window.PointsLabel.Text = Loc.GetString("rmc-xeno-ui-evolution-points",
            ("points", (int)Math.Floor(points.Double())),
            ("maxPoints", xeno.Max));

        if (lackingOvipositor && xeno.Max > FixedPoint2.Zero)
        {
            if (!_window.OvipositorNeededLabel.Visible)
            {
                _window.OvipositorNeededLabel.SetMarkupPermissive(Loc.GetString("rmc-xeno-ui-ovi-needed-label"));
                _window.OvipositorNeededLabel.Visible = true;
            }
        }
        else if (_window.OvipositorNeededLabel.Visible)
        {
            _window.OvipositorNeededLabel.Visible = false;
        }

        RefreshLottery(lotteryChoices);
    }

    private void RefreshLottery(List<EntProtoId>? lotteryChoices)
    {
        if (_window == null)
            return;

        foreach (var control in _lotteryControls.Values)
            control.Visible = false;

        var lotteryOpen = false;
        if (lotteryChoices is { Count: > 0 } choices)
        {
            lotteryOpen = true;

            foreach (var choice in choices)
                AddLottery(choice);

            _window.LotteryLabel.SetMarkupPermissive(Loc.GetString("rmc-xeno-ui-lottery-label"));
        }

        EntMan.TryGetComponent(Owner, out XenoLotteryRegistrationComponent? registration);
        foreach (var (id, control) in _lotteryControls)
            control.Button.Pressed = registration != null && registration.Target == id;

        _window.LotteryLabel.Visible = lotteryOpen;
        _window.LotterySeparator.Visible = lotteryOpen &&
                                           (_window.EvolutionsContainer.Children.Any(child => child.Visible) ||
                                            _window.StrainsContainer.Children.Any(child => child.Visible));
    }
}
