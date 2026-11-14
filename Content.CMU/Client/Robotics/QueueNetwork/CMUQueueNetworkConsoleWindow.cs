using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.CMU14.Interface;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.Robotics.QueueNetwork;

/// <summary>
/// CRT-style production diagnostic terminal. The player selects nodes,
/// analyzes them to reveal lambda/rho, upgrades service rates and projects the
/// queue chart until every node has a steady state.
/// </summary>
public sealed class CMUQueueNetworkConsoleWindow : DefaultWindow
{
    public event Action<string>? OnAnalyze;
    public event Action<string>? OnUpgrade;
    public event Action? OnProject;
    public event Action? OnSubmit;
    public event Action? OnNewContract;
    public event Action? OnResetUpgrades;

    private readonly CrtConsoleKit _kit = new();
    private readonly BoxContainer _body;
    private readonly RichTextLabel _status;
    private readonly Label _subheading;
    private readonly Label _budgetValue;
    private readonly Label _timeValue;
    private readonly Label _rewardValue;

    private CMUQueueNetworkConsoleState? _state;
    private string? _selectedNodeId;

    public CMUQueueNetworkConsoleWindow()
    {
        Title = Loc.GetString("cmu-queue-console-title");
        MinSize = new Vector2(720, 620);
        SetSize = new Vector2(840, 760);

        var reference = _kit.Button(
            Loc.GetString("cmu-queue-console-reference"),
            CmuButtonStyles.Variant.Neutral);
        reference.OnPressed += _ => OpenFormulas();

        var header = _kit.Header(
            Loc.GetString("cmu-queue-console-heading"),
            out _subheading,
            reference);
        _kit.Root.AddChild(header);

        var readouts = CrtConsoleKit.Row(6);
        readouts.AddChild(_kit.Readout(
            Loc.GetString("cmu-queue-console-budget"),
            "—",
            out _budgetValue));
        readouts.AddChild(_kit.Readout(
            Loc.GetString("cmu-queue-console-time"),
            "—",
            out _timeValue));
        readouts.AddChild(_kit.Readout(
            Loc.GetString("cmu-queue-console-reward"),
            "—",
            out _rewardValue));
        _kit.Root.AddChild(readouts);

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        _body = CrtConsoleKit.Column(8);
        _body.HorizontalExpand = true;
        _body.Margin = new Thickness(0, 0, 6, 0);
        scroll.AddChild(_body);
        _kit.Root.AddChild(scroll);

        var footer = CrtConsoleKit.Strip(CrtTerminalPalette.Surface1);
        _status = CrtConsoleKit.Wrapping(string.Empty, CrtTerminalPalette.TextDim);
        footer.AddChild(_status);
        _kit.Root.AddChild(footer);

        Contents.AddChild(_kit.Tube);
    }

    public void UpdateState(CMUQueueNetworkConsoleState state)
    {
        _state = state;

        _subheading.Text = LocOrKey(
            state.NetworkName,
            Loc.GetString("cmu-queue-console-subheading"));

        var color = state.StatusKind switch
        {
            CMUQueueStatusKind.Good => CrtTerminalPalette.Accent,
            CMUQueueStatusKind.Bad => CrtTerminalPalette.Alert,
            _ => CrtTerminalPalette.TextDim,
        };

        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(state.Status);
        message.Pop();
        _status.SetMessage(message);

        if (state.Nodes.Count == 0 && !state.Active && !state.Completed)
        {
            _budgetValue.Text = "—";
            _timeValue.Text = "—";
            _rewardValue.Text = "—";
        }
        else
        {
            _budgetValue.Text = CrtConsoleKit.Money(state.Budget);
            _timeValue.Text = Loc.GetString(
                "cmu-queue-console-time-value",
                ("left", state.TimeLeft),
                ("total", state.InitialTime));
            _rewardValue.Text = CrtConsoleKit.Money(state.Reward);
        }

        if (state.Nodes.Count > 0 &&
            (_selectedNodeId == null || state.Nodes.All(node => node.Id != _selectedNodeId)))
        {
            _selectedNodeId = state.Nodes[0].Id;
        }

        RebuildBody();
    }

    private void RebuildBody()
    {
        _body.RemoveAllChildren();
        if (_state == null)
            return;

        if (!_state.Active && !_state.Completed && !_state.Failed)
        {
            BuildIdle(_state);
            return;
        }

        BuildContract(_state);
    }

    private void BuildIdle(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-idle"), out var column);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-idle-desc"),
            CrtTerminalPalette.TextDim));

        var request = _kit.Button(
            Loc.GetString("cmu-queue-console-request"),
            CmuButtonStyles.Variant.Affirm);
        request.OnPressed += _ => OnNewContract?.Invoke();
        column.AddChild(request);

        _body.AddChild(section);
    }

    private void BuildContract(CMUQueueNetworkConsoleState state)
    {
        var description = LocOrKey(state.NetworkDescription, string.Empty);
        if (!string.IsNullOrEmpty(description))
        {
            var overview = _kit.Section(Loc.GetString("cmu-queue-console-overview"), out var overviewColumn);
            overviewColumn.AddChild(CrtConsoleKit.Wrapping(description, CrtTerminalPalette.TextDim));
            _body.AddChild(overview);
        }

        if (state.Completed)
        {
            var completed = _kit.Section(Loc.GetString("cmu-queue-console-completed"), out var completedColumn);
            completedColumn.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-queue-console-completed-hint", ("reward", state.Reward)),
                CrtTerminalPalette.Accent));
            _body.AddChild(completed);
        }

        _body.AddChild(BuildProjection(state));
        _body.AddChild(BuildNodeGrid(state));

        if (state.Nodes.FirstOrDefault(node => node.Id == _selectedNodeId) is { } selected)
            _body.AddChild(BuildNodeDetails(state, selected));

        _body.AddChild(BuildRoutes(state));
        _body.AddChild(BuildActions(state));
    }

    private PanelContainer BuildNodeGrid(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-line"), out var column);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-line-hint"),
            CrtTerminalPalette.TextDim));

        if (state.Nodes.Count == 0)
            return section;

        var columns = state.Nodes.Max(node => node.Column) + 1;
        var rows = state.Nodes.Max(node => node.Row) + 1;
        var grid = new GridContainer
        {
            Columns = columns,
            HorizontalExpand = true,
        };

        for (var row = 0; row < rows; row++)
        {
            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                var node = state.Nodes.FirstOrDefault(candidate =>
                    candidate.Column == columnIndex && candidate.Row == row);

                if (node == null)
                {
                    grid.AddChild(new Control { MinSize = new Vector2(0, 34) });
                    continue;
                }

                var variant = !node.Analyzed
                    ? CmuButtonStyles.Variant.Neutral
                    : node.Stable
                        ? CmuButtonStyles.Variant.Affirm
                        : CmuButtonStyles.Variant.Danger;

                var label = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}{1}  Q {2}",
                    node.Id == _selectedNodeId ? "> " : string.Empty,
                    NodeName(node),
                    FormatShort(node.Queue));

                var button = _kit.Button(label, variant);
                button.ToolTip = Loc.GetString("cmu-queue-console-node-tooltip");
                button.HorizontalExpand = true;
                var nodeId = node.Id;
                button.OnPressed += _ => SelectNode(nodeId);
                grid.AddChild(button);
            }
        }

        column.AddChild(grid);
        return section;
    }

    private PanelContainer BuildNodeDetails(
        CMUQueueNetworkConsoleState state,
        CMUQueueNodeInfo node)
    {
        var accent = !node.Analyzed
            ? (Color?) null
            : node.Stable
                ? CrtTerminalPalette.Accent
                : CrtTerminalPalette.Alert;
        var section = _kit.Section(NodeName(node), out var column, accent);

        column.AddChild(_kit.Field(
            Loc.GetString("cmu-queue-console-service"),
            FormatRate(node.ServiceRate)));
        column.AddChild(_kit.Field(
            Loc.GetString("cmu-queue-console-external"),
            FormatRate(node.ExternalRate)));
        column.AddChild(_kit.Field(
            Loc.GetString("cmu-queue-console-queue"),
            FormatNumber(node.Queue)));
        column.AddChild(_kit.Field(
            Loc.GetString("cmu-queue-console-upgrades"),
            $"{node.Upgrades}/{node.MaxUpgrades}"));

        if (node.Analyzed)
        {
            var metricColor = node.Stable
                ? CrtTerminalPalette.TextBright
                : CrtTerminalPalette.Alert;

            column.AddChild(_kit.Field(
                Loc.GetString("cmu-queue-console-lambda"),
                FormatRate(node.Lambda)));
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-queue-console-rho"),
                FormatRho(node.Rho),
                node.Stable ? CrtTerminalPalette.Accent : CrtTerminalPalette.Alert));
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-queue-console-l"),
                FormatNumber(node.L),
                metricColor));
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-queue-console-lq"),
                FormatNumber(node.Lq),
                metricColor));
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-queue-console-w"),
                FormatMinutes(node.W),
                metricColor));
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-queue-console-wq"),
                FormatMinutes(node.Wq),
                metricColor));
            column.AddChild(CrtConsoleKit.Wrapping(
                node.Stable
                    ? Loc.GetString(
                        "cmu-queue-console-stable-hint",
                        ("lambda", FormatRate(node.Lambda)))
                    : Loc.GetString("cmu-queue-console-unstable-hint"),
                node.Stable ? CrtTerminalPalette.Accent : CrtTerminalPalette.Alert));
        }
        else
        {
            column.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-queue-console-no-data"),
                CrtTerminalPalette.TextDim));

            var analyze = _kit.Button(
                Loc.GetString(
                    "cmu-queue-console-analyze",
                    ("cost", state.AnalysisCost)),
                CmuButtonStyles.Variant.Affirm);
            analyze.Disabled = !state.Active || state.TimeLeft < state.AnalysisCost;
            analyze.OnPressed += _ => OnAnalyze?.Invoke(node.Id);
            column.AddChild(analyze);
        }

        var newRate = node.ServiceRate * node.UpgradeMultiplier;
        var upgradeText = node.Upgrades >= node.MaxUpgrades
            ? Loc.GetString("cmu-queue-console-max-upgrades")
            : Loc.GetString(
                "cmu-queue-console-upgrade",
                ("rate", FormatRate(newRate)),
                ("cost", CrtConsoleKit.Money(node.UpgradeCost)));

        var upgrade = _kit.Button(upgradeText, CmuButtonStyles.Variant.Affirm);
        upgrade.Disabled = !state.Active ||
                          node.Upgrades >= node.MaxUpgrades ||
                          state.Budget < node.UpgradeCost;
        upgrade.OnPressed += _ => OnUpgrade?.Invoke(node.Id);
        column.AddChild(upgrade);

        return section;
    }

    private PanelContainer BuildRoutes(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-routes"), out var column);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-routes-hint"),
            CrtTerminalPalette.TextDim));

        foreach (var route in state.Routes)
        {
            var from = NodeName(state, route.From);
            var to = string.IsNullOrEmpty(route.To) || route.To == "sink"
                ? Loc.GetString("cmu-queue-console-sink")
                : NodeName(state, route.To);

            column.AddChild(CrtConsoleKit.Wrapping(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} → {1}: {2:0}%",
                    from,
                    to,
                    route.Probability * 100),
                CrtTerminalPalette.Text));
        }

        return section;
    }

    private PanelContainer BuildProjection(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-projection"), out var column);

        var graph = new CMUQueueHistoryControl();
        graph.SetData(state.Nodes, state.History, state.Elapsed);
        column.AddChild(graph);

        var legend = new GridContainer
        {
            Columns = Math.Max(1, Math.Min(3, state.Nodes.Count)),
            HorizontalExpand = true,
        };
        for (var index = 0; index < state.Nodes.Count; index++)
        {
            legend.AddChild(_kit.Chip(
                NodeName(state.Nodes[index]),
                CMUQueueHistoryControl.ColorFor(index)));
        }

        column.AddChild(legend);

        var project = _kit.Button(
            Loc.GetString(
                "cmu-queue-console-project",
                ("minutes", state.ProjectionMinutes),
                ("cost", state.ProjectionCost)),
            CmuButtonStyles.Variant.Affirm);
        project.Disabled = !state.Active || state.TimeLeft < state.ProjectionCost;
        project.OnPressed += _ => OnProject?.Invoke();
        column.AddChild(project);

        return section;
    }

    private PanelContainer BuildActions(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-actions"), out var column);

        var submit = _kit.Button(
            Loc.GetString("cmu-queue-console-submit"),
            CmuButtonStyles.Variant.Affirm);
        submit.Disabled = !state.Active;
        submit.OnPressed += _ => OnSubmit?.Invoke();
        column.AddChild(submit);

        var reset = _kit.Button(
            Loc.GetString("cmu-queue-console-reset"),
            CmuButtonStyles.Variant.Caution);
        reset.Disabled = !state.Active;
        reset.OnPressed += _ => OnResetUpgrades?.Invoke();
        column.AddChild(reset);

        var newContract = _kit.Button(
            Loc.GetString("cmu-queue-console-new-contract"),
            state.Active
                ? CmuButtonStyles.Variant.Danger
                : CmuButtonStyles.Variant.Neutral);
        newContract.OnPressed += _ => OnNewContract?.Invoke();
        column.AddChild(newContract);

        return section;
    }

    private void SelectNode(string nodeId)
    {
        _selectedNodeId = nodeId;
        RebuildBody();
    }

    private void OpenFormulas()
    {
        new CMUQueueFormulasWindow().OpenCentered();
    }

    private static string NodeName(CMUQueueNodeInfo node)
    {
        return LocOrKey(node.Name, node.Id);
    }

    private static string NodeName(CMUQueueNetworkConsoleState state, string nodeId)
    {
        var node = state.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
        return node == null ? nodeId : NodeName(node);
    }

    private static string LocOrKey(string key, string fallback)
    {
        if (string.IsNullOrEmpty(key))
            return fallback;

        return Loc.TryGetString(key, out var value) ? value : key;
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value))
            return "—";
        if (double.IsPositiveInfinity(value))
            return "∞";
        if (double.IsNegativeInfinity(value))
            return "-∞";

        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatRate(double value)
    {
        return FormatNumber(value) + "/min";
    }

    private static string FormatShort(double value)
    {
        if (double.IsNaN(value))
            return "—";
        if (double.IsPositiveInfinity(value))
            return "∞";
        if (double.IsNegativeInfinity(value))
            return "-∞";

        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string FormatRho(double value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} ({1:0}%)",
            FormatNumber(value),
            value * 100);
    }

    private static string FormatMinutes(double value)
    {
        return FormatNumber(value) + " min";
    }
}
