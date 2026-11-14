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
/// CRT-style production diagnostic terminal. The layout uses full-width rows
/// and wrapping labels so longer translations do not overlap or clip.
/// </summary>
public sealed class CMUQueueNetworkConsoleWindow : DefaultWindow
{
    public event Action<string>? OnAnalyze;
    public event Action<string, double>? OnSetServiceRate;
    public event Action<string>? OnReroute;
    public event Action? OnProject;
    public event Action? OnSubmit;
    public event Action? OnNewContract;
    public event Action? OnResetInterventions;

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
        MinSize = new Vector2(720, 560);
        SetSize = new Vector2(900, 720);

        var reference = _kit.Button(
            Loc.GetString("cmu-queue-console-reference"),
            CmuButtonStyles.Variant.Neutral);
        reference.OnPressed += _ => OpenFormulas();

        var header = _kit.Header(
            Loc.GetString("cmu-queue-console-heading"),
            out _subheading,
            reference);
        _kit.Root.AddChild(header);

        var readouts = new GridContainer
        {
            Columns = 3,
            HSeparationOverride = 6,
            HorizontalExpand = true,
        };
        readouts.AddChild(BuildReadout(
            Loc.GetString("cmu-queue-console-budget"),
            out _budgetValue));
        readouts.AddChild(BuildReadout(
            Loc.GetString("cmu-queue-console-time"),
            out _timeValue));
        readouts.AddChild(BuildReadout(
            Loc.GetString("cmu-queue-console-reward"),
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

        if (state.Nodes.Count > 0
            && (_selectedNodeId == null || state.Nodes.All(node => node.Id != _selectedNodeId)))
            _selectedNodeId = state.Nodes[0].Id;

        RebuildBody();
    }

    private PanelContainer BuildReadout(string caption, out Label value)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = CrtTerminalPalette.Surface2,
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4,
            },
        };
        var column = CrtConsoleKit.Column(0);
        column.AddChild(CrtConsoleKit.Wrapping(
            caption.ToUpperInvariant(),
            CrtTerminalPalette.TextDim));
        value = CrtConsoleKit.Text("—", _kit.Title, CrtTerminalPalette.TextBright);
        column.AddChild(value);
        panel.AddChild(column);
        return panel;
    }

    private void RebuildBody()
    {
        _body.RemoveAllChildren();
        if (_state == null)
            return;

        if (!_state.Active && !_state.Completed)
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

        _body.AddChild(BuildTopology(state));
        _body.AddChild(BuildProjection(state));
        _body.AddChild(BuildNodeList(state));

        if (state.Nodes.FirstOrDefault(node => node.Id == _selectedNodeId) is { } selected)
            _body.AddChild(BuildNodeDetails(state, selected));

        _body.AddChild(BuildRoutes(state));

        if (state.Bypasses.Count > 0)
            _body.AddChild(BuildBypasses(state));

        _body.AddChild(BuildActions(state));
    }

    private PanelContainer BuildNodeList(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-line"), out var column);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-line-hint"),
            CrtTerminalPalette.TextDim));

        foreach (var node in state.Nodes)
        {
            var variant = !node.Analyzed
                ? CmuButtonStyles.Variant.Neutral
                : node.Stable
                    ? CmuButtonStyles.Variant.Affirm
                    : CmuButtonStyles.Variant.Danger;

            var button = _kit.Button(
                Loc.GetString(
                    "cmu-queue-console-node-label",
                    ("selected", node.Id == _selectedNodeId ? "> " : string.Empty),
                    ("name", NodeName(node)),
                    ("queue", FormatShort(node.Queue))),
                variant);
            button.ToolTip = Loc.GetString("cmu-queue-console-node-tooltip");
            button.HorizontalExpand = true;
            var nodeId = node.Id;
            button.OnPressed += _ => SelectNode(nodeId);
            column.AddChild(button);
        }

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

        var grid = NewFieldGrid();
        AddField(grid, Loc.GetString("cmu-queue-console-service"), FormatRate(node.ServiceRate));
        AddField(
            grid,
            Loc.GetString("cmu-queue-console-rate-range"),
            Loc.GetString(
                "cmu-queue-console-rate-range-value",
                ("base", FormatRate(node.BaseServiceRate)),
                ("max", FormatRate(node.MaxServiceRate))));
        AddField(grid, Loc.GetString("cmu-queue-console-invested"), CrtConsoleKit.Money(node.Invested));
        AddField(grid, Loc.GetString("cmu-queue-console-full-upgrade"), CrtConsoleKit.Money(node.UpgradeCost));
        AddField(grid, Loc.GetString("cmu-queue-console-external"), FormatRate(node.ExternalRate));
        AddField(grid, Loc.GetString("cmu-queue-console-queue"), FormatNumber(node.Queue));
        column.AddChild(grid);

        if (node.Analyzed)
        {
            var metricColor = node.Stable
                ? CrtTerminalPalette.TextBright
                : CrtTerminalPalette.Alert;

            var metrics = NewFieldGrid();
            AddField(metrics, Loc.GetString("cmu-queue-console-lambda"), FormatRate(node.Lambda));
            AddField(
                metrics,
                Loc.GetString("cmu-queue-console-rho"),
                FormatRho(node.Rho),
                node.Stable ? CrtTerminalPalette.Accent : CrtTerminalPalette.Alert);
            AddField(metrics, Loc.GetString("cmu-queue-console-l"), FormatNumber(node.L), metricColor);
            AddField(metrics, Loc.GetString("cmu-queue-console-lq"), FormatNumber(node.Lq), metricColor);
            AddField(metrics, Loc.GetString("cmu-queue-console-w"), FormatMinutes(node.W), metricColor);
            AddField(metrics, Loc.GetString("cmu-queue-console-wq"), FormatMinutes(node.Wq), metricColor);
            column.AddChild(metrics);

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
            var noData = CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-queue-console-no-data"),
                CrtTerminalPalette.TextDim);
            noData.Margin = new Thickness(0, 6, 0, 0);
            column.AddChild(noData);

            var analyze = _kit.Button(
                Loc.GetString(
                    "cmu-queue-console-analyze",
                    ("cost", state.AnalysisCost)),
                CmuButtonStyles.Variant.Affirm);
            analyze.Disabled = !state.Active || state.TimeLeft < state.AnalysisCost;
            analyze.OnPressed += _ => OnAnalyze?.Invoke(node.Id);
            column.AddChild(analyze);
        }

        var rateRow = CrtConsoleKit.Row(6);
        var rateEdit = new LineEdit
        {
            Text = node.ServiceRate.ToString("0.##", CultureInfo.CurrentCulture),
            HorizontalExpand = true,
            MinSize = new Vector2(100, 0),
        };
        var costPreview = CrtConsoleKit.Wrapping(string.Empty, CrtTerminalPalette.TextDim);
        var apply = _kit.Button(
            Loc.GetString("cmu-queue-console-set-rate"),
            CmuButtonStyles.Variant.Affirm);
        var max = _kit.Button(
            Loc.GetString("cmu-queue-console-set-rate-max"),
            CmuButtonStyles.Variant.Neutral);

        void UpdatePreview()
        {
            if (!TryParseRate(rateEdit.Text, out var target)
                || target <= node.ServiceRate + 0.001)
            {
                SetRichText(
                    costPreview,
                    Loc.GetString("cmu-queue-console-set-rate-invalid"),
                    CrtTerminalPalette.TextDim);
                apply.Disabled = true;
                return;
            }

            target = Math.Min(target, node.MaxServiceRate);
            var cost = target >= node.MaxServiceRate - 0.001
                ? node.UpgradeCost
                : Math.Max(
                    1,
                    (int) Math.Ceiling((target - node.ServiceRate) * node.CostPerMu - 0.0001));
            var affordable = cost <= state.Budget;
            SetRichText(
                costPreview,
                Loc.GetString(
                    "cmu-queue-console-set-rate-cost",
                    ("cost", CrtConsoleKit.Money(cost))),
                affordable ? CrtTerminalPalette.TextDim : CrtTerminalPalette.Alert);
            apply.Disabled = !state.Active || !affordable;
        }

        rateEdit.OnTextChanged += _ => UpdatePreview();
        apply.OnPressed += _ =>
        {
            if (!TryParseRate(rateEdit.Text, out var target))
                return;

            OnSetServiceRate?.Invoke(node.Id, Math.Min(target, node.MaxServiceRate));
        };
        max.OnPressed += _ =>
        {
            rateEdit.Text = node.MaxServiceRate.ToString("0.##", CultureInfo.CurrentCulture);
            UpdatePreview();
        };

        rateRow.AddChild(rateEdit);
        rateRow.AddChild(apply);
        rateRow.AddChild(max);
        column.AddChild(new Control { MinHeight = 4 });
        column.AddChild(rateRow);
        column.AddChild(costPreview);
        UpdatePreview();

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
                Loc.GetString(
                    "cmu-queue-console-route-line",
                    ("from", from),
                    ("to", to),
                    ("percent", (route.Probability * 100).ToString("0", CultureInfo.CurrentCulture))),
                CrtTerminalPalette.Text));
        }

        return section;
    }

    private PanelContainer BuildBypasses(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-bypasses"), out var column);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-bypasses-hint"),
            CrtTerminalPalette.TextDim));

        foreach (var bypass in state.Bypasses)
        {
            var from = NodeName(state, bypass.From);
            var to = NodeName(state, bypass.To);
            var grid = NewFieldGrid();
            AddField(grid, Loc.GetString("cmu-queue-console-bypass-route"), $"{from} → {to}");
            AddField(
                grid,
                Loc.GetString("cmu-queue-console-bypass-progress"),
                Loc.GetString(
                    "cmu-queue-console-bypass-progress-value",
                    ("current", (bypass.CurrentPercent * 100).ToString("0", CultureInfo.CurrentCulture)),
                    ("max", (bypass.MaxPercent * 100).ToString("0", CultureInfo.CurrentCulture))));
            AddField(
                grid,
                Loc.GetString("cmu-queue-console-bypass-cost"),
                CrtConsoleKit.Money(bypass.NextCost));
            column.AddChild(grid);

            var action = bypass.Maxed
                ? Loc.GetString("cmu-queue-console-bypass-maxed")
                : Loc.GetString(
                    "cmu-queue-console-bypass-action",
                    ("percent", (bypass.StepPercent * 100).ToString("0", CultureInfo.CurrentCulture)),
                    ("to", to),
                    ("cost", CrtConsoleKit.Money(bypass.NextCost)));

            var reroute = _kit.Button(action, CmuButtonStyles.Variant.Affirm);
            reroute.Disabled = !state.Active || !bypass.CanAfford || bypass.Maxed;
            var bypassId = bypass.Id;
            reroute.OnPressed += _ => OnReroute?.Invoke(bypassId);
            column.AddChild(reroute);
        }

        return section;
    }

    private PanelContainer BuildTopology(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-schematic"), out var column);
        var topology = new CMUQueueTopologyControl
        {
            EmptyText = Loc.GetString("cmu-queue-console-schematic-empty"),
        };
        topology.SetData(state.Nodes, state.Routes, state.Bypasses);
        column.AddChild(topology);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-schematic-hint"),
            CrtTerminalPalette.TextDim));
        return section;
    }

    private PanelContainer BuildProjection(CMUQueueNetworkConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-queue-console-projection"), out var column);

        var graph = new CMUQueueHistoryControl
        {
            EmptyText = Loc.GetString("cmu-queue-console-no-projection"),
            MinHeight = 220,
        };
        graph.SetData(state.Nodes, state.History, state.Elapsed);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-projection-caption"),
            CrtTerminalPalette.TextDim));
        column.AddChild(graph);

        var lastPoint = state.History.LastOrDefault();
        var legend = new GridContainer
        {
            Columns = Math.Max(1, Math.Min(2, state.Nodes.Count)),
            HorizontalExpand = true,
        };
        for (var index = 0; index < state.Nodes.Count; index++)
        {
            var queue = lastPoint != null && index < lastPoint.Queues.Count
                ? lastPoint.Queues[index]
                : (float) state.Nodes[index].Queue;
            legend.AddChild(_kit.Chip(
                Loc.GetString(
                    "cmu-queue-console-legend-entry",
                    ("name", NodeName(state.Nodes[index])),
                    ("queue", FormatShort(queue))),
                CMUQueueHistoryControl.ColorFor(index)));
        }

        column.AddChild(legend);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-console-projection-hint"),
            CrtTerminalPalette.TextDim));

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
        reset.OnPressed += _ => OnResetInterventions?.Invoke();
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

    private static GridContainer NewFieldGrid()
    {
        return new GridContainer
        {
            Columns = 2,
            HSeparationOverride = 8,
            VSeparationOverride = 2,
            HorizontalExpand = true,
        };
    }

    private static void AddField(
        GridContainer grid,
        string label,
        string value,
        Color? color = null)
    {
        var key = CrtConsoleKit.BoldWrapping(
            label.ToUpperInvariant(),
            CrtTerminalPalette.TextDim);
        key.MinWidth = 150;
        grid.AddChild(key);
        grid.AddChild(CrtConsoleKit.Wrapping(
            value,
            color ?? CrtTerminalPalette.TextBright));
    }

    private void SelectNode(string nodeId)
    {
        _selectedNodeId = nodeId;
        RebuildBody();
    }

    private void OpenFormulas()
    {
        var window = new CMUQueueFormulasWindow();
        window.OpenCentered();
    }

    private static string NodeName(CMUQueueNodeInfo node)
    {
        var name = LocOrKey(node.Name, node.Id);
        return string.IsNullOrEmpty(node.Suffix) ? name : $"{name} {node.Suffix}";
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

        return Loc.TryGetString(key, out var value) ? value : fallback;
    }

    private static void SetRichText(RichTextLabel label, string text, Color color)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        label.SetMessage(message);
    }

    private static bool TryParseRate(string text, out double rate)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out rate)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out rate);
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value))
            return "—";
        if (double.IsPositiveInfinity(value))
            return "∞";
        if (double.IsNegativeInfinity(value))
            return "-∞";

        return value.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static string FormatShort(double value)
    {
        if (double.IsNaN(value))
            return "—";
        if (double.IsPositiveInfinity(value))
            return "∞";
        if (double.IsNegativeInfinity(value))
            return "-∞";

        return value.ToString("0.0", CultureInfo.CurrentCulture);
    }

    private static string FormatRate(double value)
    {
        return Loc.GetString("cmu-queue-console-rate", ("value", FormatNumber(value)));
    }

    private static string FormatRho(double value)
    {
        return Loc.GetString(
            "cmu-queue-console-rho-value",
            ("rho", FormatNumber(value)),
            ("percent", (value * 100).ToString("0", CultureInfo.CurrentCulture)));
    }

    private static string FormatMinutes(double value)
    {
        return Loc.GetString("cmu-queue-console-duration", ("value", FormatNumber(value)));
    }
}
