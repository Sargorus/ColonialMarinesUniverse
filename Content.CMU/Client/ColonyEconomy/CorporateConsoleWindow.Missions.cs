using System;
using System.Linq;
using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Content.Shared.CMU14.ColonyEconomy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleWindow
{
    private void BuildMissionTab()
    {
        _missionKit.Root.AddChild(_missionKit.Header(
            Loc.GetString("cmu-corporate-missions-heading"), out var subheading));
        subheading.Text = Loc.GetString("cmu-corporate-missions-subheading");

        var readouts = new GridContainer
        {
            Columns = 3,
            HSeparationOverride = 6,
            HorizontalExpand = true,
        };
        readouts.AddChild(_missionKit.Readout(
            Loc.GetString("cmu-corporate-science-chemistry"), "0", out _missionChemistry,
            CrtTerminalPalette.Accent));
        readouts.AddChild(_missionKit.Readout(
            Loc.GetString("cmu-corporate-science-robotics"), "0", out _missionRobotics,
            CrtTerminalPalette.Caution));
        readouts.AddChild(_missionKit.Readout(
            Loc.GetString("cmu-corporate-science-company-value"), "0", out _missionCompanyValue,
            CrtTerminalPalette.Accent));
        _missionKit.Root.AddChild(readouts);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _missionBody = CrtConsoleKit.Column(8);
        _missionBody.HorizontalExpand = true;
        _missionBody.Margin = new Thickness(0, 0, 6, 0);
        scroll.AddChild(_missionBody);
        _missionKit.Root.AddChild(scroll);

        var footer = CrtConsoleKit.Strip(CrtTerminalPalette.Surface1);
        _missionStatus = CrtConsoleKit.Wrapping(string.Empty, CrtTerminalPalette.TextDim);
        footer.AddChild(_missionStatus);
        _missionKit.Root.AddChild(footer);
    }

    private void BuildMissions(CorporateConsoleBuiState state)
    {
        _missionBody.RemoveAllChildren();

        SetRichText(_missionStatus, state.MissionStatus, CrtTerminalPalette.TextDim);

        if (state.ActiveMissions.Count > 0)
        {
            _missionBody.AddChild(_missionKit.Section(
                Loc.GetString("cmu-corporate-missions-active"), out var active));
            foreach (var mission in state.ActiveMissions)
                active.AddChild(MissionCard(mission));
        }

        if (state.AvailableMissions.Count > 0)
        {
            _missionBody.AddChild(_missionKit.Section(
                Loc.GetString("cmu-corporate-missions-available"), out var available));
            foreach (var mission in state.AvailableMissions)
                available.AddChild(MissionCard(mission));
        }

        if (state.MissionHistory.Count > 0)
        {
            _missionBody.AddChild(_missionKit.Section(
                Loc.GetString("cmu-corporate-missions-history"), out var history));
            foreach (var mission in state.MissionHistory)
                history.AddChild(MissionCard(mission));
        }

        if (state.ActiveMissions.Count == 0
            && state.AvailableMissions.Count == 0
            && state.MissionHistory.Count == 0)
        {
            _missionBody.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-corporate-missions-empty"), CrtTerminalPalette.TextDim));
        }
    }

    private Control MissionCard(CompanyMissionInfo mission)
    {
        var accent = mission.Status switch
        {
            CompanyMissionStatus.Active => CrtTerminalPalette.Caution,
            CompanyMissionStatus.Completed => CrtTerminalPalette.Accent,
            CompanyMissionStatus.Failed => CrtTerminalPalette.Alert,
            _ => CrtTerminalPalette.Accent,
        };

        var card = _missionKit.Section(mission.Name, out var column, accent);
        column.AddChild(CrtConsoleKit.Wrapping(mission.Description, CrtTerminalPalette.TextDim));
        column.AddChild(_missionKit.Field(
            Loc.GetString("cmu-corporate-missions-objective"), mission.Objective));

        if (mission.Status == CompanyMissionStatus.Active)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-reward"),
                Loc.GetString("cmu-corporate-missions-reward-value", ("reward", mission.Reward)),
                CrtTerminalPalette.Accent));
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-progress"),
                Loc.GetString(
                    "cmu-corporate-missions-progress-value",
                    ("progress", mission.Progress),
                    ("target", mission.Target)),
                CrtTerminalPalette.Accent));
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-time"),
                Loc.GetString(
                    "cmu-corporate-missions-time-value",
                    ("seconds", mission.SecondsRemaining)),
                mission.SecondsRemaining < 120 ? CrtTerminalPalette.Alert : CrtTerminalPalette.TextBright));
        }

        if (mission.Status == CompanyMissionStatus.Available)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-reward"),
                Loc.GetString("cmu-corporate-missions-reward-value", ("reward", mission.Reward)),
                CrtTerminalPalette.Accent));
            var accept = _missionKit.Button(
                Loc.GetString("cmu-corporate-missions-accept"), CmuButtonStyles.Variant.Affirm);
            accept.OnPressed += _ => OnAcceptMission?.Invoke(mission.Id);
            column.AddChild(accept);
        }
        else if (mission.Status == CompanyMissionStatus.Completed)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-reward"),
                Loc.GetString("cmu-corporate-missions-reward-value", ("reward", mission.Reward)),
                CrtTerminalPalette.Accent));
        }
        else if (mission.Status == CompanyMissionStatus.Failed)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-status"),
                Loc.GetString("cmu-corporate-missions-failed"),
                CrtTerminalPalette.Alert));
        }

        return card;
    }

    private static string MissionSignature(CompanyMissionInfo mission)
    {
        return $"{mission.Id}:{mission.Status}:{mission.Progress}:{mission.Target}:{mission.Reward}:" +
               $"{mission.SecondsRemaining}:{mission.Objective}";
    }
}
