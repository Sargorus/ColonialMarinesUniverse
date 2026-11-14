using System.Linq;
using System.Numerics;
using Content.Shared.CMU14.Configuration;
using Content.Shared.CMU14.DroneOperator;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client.CMU14.DroneOperator;

public sealed class CMUDroneConfigurationWindow : DefaultWindow
{
    public event Action<string, HashSet<string>, HashSet<string>>? OnApply;
    public event Action<CMUUtilityDroneConfigMsg>? OnUtilityAction;

    private readonly OptionButton _faction;
    private readonly BoxContainer _channels;
    private readonly BoxContainer _access;
    private readonly Label _accessHint;
    private readonly Button _apply;

    private readonly BoxContainer _utilitySection;
    private readonly CheckBox _programmable;
    private readonly CheckBox _boardPortLocked;
    private readonly Label _utilityStatus;
    private readonly Button _restart;
    private readonly Button _pause;
    private readonly Button _resume;
    private readonly Button _stop;
    private readonly Button _eject;

    private readonly Dictionary<string, CheckBox> _channelChecks = new();
    private readonly Dictionary<string, CheckBox> _accessChecks = new();
    private List<CMUConfigurationOption> _factions = new();
    private string _selectedFaction = string.Empty;
    private bool _updating;

    public CMUDroneConfigurationWindow()
    {
        Title = Loc.GetString("cmu-drone-config-title");
        MinSize = new Vector2(440, 620);
        SetSize = new Vector2(520, 780);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };
        Contents.AddChild(root);

        _utilitySection = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };
        root.AddChild(_utilitySection);
        _utilitySection.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-config-heading") });

        _programmable = new CheckBox
        {
            Text = Loc.GetString("cmu-utility-drone-config-programmable"),
        };
        _programmable.OnToggled += args => SendUtilityAction(
            CMUUtilityDroneConfigAction.SetProgrammableMode,
            args.Pressed);
        _utilitySection.AddChild(_programmable);

        _boardPortLocked = new CheckBox
        {
            Text = Loc.GetString("cmu-utility-drone-config-port"),
        };
        _boardPortLocked.OnToggled += args => SendUtilityAction(
            CMUUtilityDroneConfigAction.SetBoardPortLocked,
            args.Pressed);
        _utilitySection.AddChild(_boardPortLocked);

        _utilityStatus = new Label();
        _utilitySection.AddChild(_utilityStatus);

        var runButtons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };
        _restart = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-config-restart"),
            HorizontalExpand = true,
        };
        _restart.OnPressed += _ => SendUtilityAction(CMUUtilityDroneConfigAction.RestartProgram);
        runButtons.AddChild(_restart);

        _pause = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-config-pause"),
            HorizontalExpand = true,
        };
        _pause.OnPressed += _ => SendUtilityAction(CMUUtilityDroneConfigAction.PauseProgram);
        runButtons.AddChild(_pause);

        _resume = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-config-resume"),
            HorizontalExpand = true,
        };
        _resume.OnPressed += _ => SendUtilityAction(CMUUtilityDroneConfigAction.ResumeProgram);
        runButtons.AddChild(_resume);
        _utilitySection.AddChild(runButtons);

        var manageButtons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };
        _stop = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-config-stop"),
            HorizontalExpand = true,
        };
        _stop.OnPressed += _ => SendUtilityAction(CMUUtilityDroneConfigAction.StopProgram);
        manageButtons.AddChild(_stop);

        _eject = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-config-eject"),
            HorizontalExpand = true,
        };
        _eject.OnPressed += _ => SendUtilityAction(CMUUtilityDroneConfigAction.EjectProgramBoard);
        manageButtons.AddChild(_eject);

        _utilitySection.AddChild(manageButtons);
        _utilitySection.AddChild(new Label
        {
            Text = Loc.GetString("cmu-utility-drone-config-board-hint"),
        });

        root.AddChild(new Label { Text = Loc.GetString("cmu-drone-config-faction") });
        _faction = new OptionButton();
        _faction.OnItemSelected += args =>
        {
            if (args.Id >= 0 && args.Id < _factions.Count)
                _selectedFaction = _factions[args.Id].Id;
        };
        root.AddChild(_faction);

        root.AddChild(new Label { Text = Loc.GetString("cmu-drone-config-channels") });
        _channels = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var channelScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        channelScroll.AddChild(_channels);
        root.AddChild(channelScroll);

        _accessHint = new Label
        {
            Text = Loc.GetString("cmu-drone-config-no-id"),
            Visible = false,
        };
        root.AddChild(_accessHint);

        root.AddChild(new Label { Text = Loc.GetString("cmu-drone-config-access") });
        _access = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var accessScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        accessScroll.AddChild(_access);
        root.AddChild(accessScroll);

        _apply = new Button { Text = Loc.GetString("cmu-drone-config-apply") };
        _apply.OnPressed += _ =>
        {
            var channels = _channelChecks.Where(pair => pair.Value.Pressed).Select(pair => pair.Key).ToHashSet();
            var access = _accessChecks.Where(pair => pair.Value.Pressed).Select(pair => pair.Key).ToHashSet();
            OnApply?.Invoke(_selectedFaction, channels, access);
        };
        root.AddChild(_apply);
    }

    public void UpdateState(CMUDroneConfigurationBuiState state)
    {
        _updating = true;
        try
        {
            _utilitySection.Visible = state.UtilityDrone;
            _programmable.Pressed = state.ProgrammableMode;
            _boardPortLocked.Pressed = state.BoardPortLocked;
            _restart.Disabled = !state.BoardInstalled || !state.ProgrammableMode;
            _pause.Disabled = !state.ProgramRunning || state.ProgramPaused;
            _resume.Disabled = !state.ProgramRunning || !state.ProgramPaused;
            _stop.Disabled = !state.ProgramRunning && !state.ProgramPaused;
            _eject.Disabled = !state.BoardInstalled;

            _utilityStatus.Text = state.BoardInstalled
                ? Loc.GetString(
                    "cmu-utility-drone-config-status",
                    ("step", state.Step),
                    ("steps", state.StepCount),
                    ("activity", string.IsNullOrEmpty(state.Activity)
                        ? Loc.GetString("cmu-utility-drone-config-idle")
                        : state.Activity))
                : Loc.GetString("cmu-utility-drone-config-no-board");

            _factions = state.Factions;
            _faction.Clear();
            foreach (var faction in _factions)
                _faction.AddItem(Loc.GetString(faction.Name));

            var factionIndex = _factions.FindIndex(faction => faction.Id == _selectedFaction);
            if (factionIndex < 0)
                factionIndex = _factions.FindIndex(faction => faction.Id == state.CurrentFaction);
            if (factionIndex < 0 && _factions.Count > 0)
                factionIndex = 0;

            if (factionIndex >= 0)
            {
                _faction.SelectId(factionIndex);
                _selectedFaction = _factions[factionIndex].Id;
            }
            else
            {
                _selectedFaction = string.Empty;
            }

            _channels.RemoveAllChildren();
            _channelChecks.Clear();
            foreach (var channel in state.Channels)
            {
                var check = new CheckBox
                {
                    Text = channel.Name,
                    Pressed = state.SelectedChannels.Contains(channel.Id),
                };
                _channelChecks[channel.Id] = check;
                _channels.AddChild(check);
            }

            _access.RemoveAllChildren();
            _accessChecks.Clear();
            foreach (var access in state.Access)
            {
                var check = new CheckBox
                {
                    Text = access.Name,
                    Pressed = state.SelectedAccess.Contains(access.Id),
                };
                _accessChecks[access.Id] = check;
                _access.AddChild(check);
            }

            _accessHint.Visible = !state.HasIdCard;
        }
        finally
        {
            _updating = false;
        }
    }

    private void SendUtilityAction(CMUUtilityDroneConfigAction action, bool enabled = false)
    {
        if (_updating)
            return;

        OnUtilityAction?.Invoke(new CMUUtilityDroneConfigMsg(action, enabled));
    }
}
