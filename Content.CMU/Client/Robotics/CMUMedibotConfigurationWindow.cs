using System.Linq;
using System.Numerics;
using Content.Shared.CMU14.Robotics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client.CMU14.Robotics;

public sealed class CMUMedibotConfigurationWindow : DefaultWindow
{
    public event Action<int, bool, bool, bool, HashSet<string>>? OnApply;

    private readonly Label _beaker;
    private readonly SpinBox _amount;
    private readonly CheckBox _brute;
    private readonly CheckBox _burn;
    private readonly CheckBox _critical;
    private readonly BoxContainer _factions;
    private readonly Dictionary<string, CheckBox> _factionChecks = new();

    public CMUMedibotConfigurationWindow()
    {
        Title = Loc.GetString("cmu-medibot-config-title");
        MinSize = new Vector2(420, 300);
        SetSize = new Vector2(460, 340);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
        };
        Contents.AddChild(root);

        _beaker = new Label();
        root.AddChild(_beaker);

        root.AddChild(new Label { Text = Loc.GetString("cmu-medibot-config-amount") });
        _amount = new SpinBox
        {
            Value = 15,
            IsValid = value => value is >= 1 and <= 50,
        };
        root.AddChild(_amount);

        _brute = new CheckBox
        {
            Text = Loc.GetString("cmu-medibot-config-brute"),
            Pressed = true,
        };
        root.AddChild(_brute);

        _burn = new CheckBox
        {
            Text = Loc.GetString("cmu-medibot-config-burn"),
            Pressed = true,
        };
        root.AddChild(_burn);

        _critical = new CheckBox
        {
            Text = Loc.GetString("cmu-medibot-config-critical"),
            Pressed = true,
        };
        root.AddChild(_critical);

        root.AddChild(new Label { Text = Loc.GetString("cmu-medibot-config-factions") });
        _factions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var factionScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        factionScroll.AddChild(_factions);
        root.AddChild(factionScroll);

        var apply = new Button { Text = Loc.GetString("cmu-medibot-config-apply") };
        apply.OnPressed += _ =>
        {
            var factions = _factionChecks
                .Where(pair => pair.Value.Pressed)
                .Select(pair => pair.Key)
                .ToHashSet();
            OnApply?.Invoke(_amount.Value, _brute.Pressed, _burn.Pressed, _critical.Pressed, factions);
        };
        root.AddChild(apply);
    }

    public void UpdateState(CMUMedibotConfigurationBuiState state)
    {
        _beaker.Text = state.HasBeaker
            ? Loc.GetString("cmu-medibot-config-beaker", ("volume", state.BeakerVolume))
            : Loc.GetString("cmu-medibot-config-no-beaker");
        _amount.OverrideValue(state.InjectAmount);
        _brute.Pressed = state.InjectOnBrute;
        _burn.Pressed = state.InjectOnBurn;
        _critical.Pressed = state.InjectOnCritical;

        _factions.RemoveAllChildren();
        _factionChecks.Clear();
        foreach (var faction in state.Factions)
        {
            var check = new CheckBox
            {
                Text = Loc.GetString(faction.Name),
                Pressed = state.SelectedFactions.Contains(faction.Id),
            };
            _factionChecks[faction.Id] = check;
            _factions.AddChild(check);
        }
    }
}
