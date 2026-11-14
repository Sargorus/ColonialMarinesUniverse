using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Server.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Runs production diagnostic contracts on the queueing-network terminal.
/// The server owns the network state, the maintenance budget and the
/// diagnostic clock; the client only renders the state and sends intents.
/// </summary>
public sealed partial class CMUQueueNetworkConsoleSystem : EntitySystem
{
    [Dependency] private CorporateConsoleSystem _corporateConsole = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUQueueNetworkConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CMUQueueNetworkConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);

        Subs.BuiEvents<CMUQueueNetworkConsoleComponent>(CMUQueueNetworkConsoleUiKey.Key, subs =>
        {
            subs.Event<CMUQueueAnalyzeNodeMessage>(OnAnalyzeNode);
            subs.Event<CMUQueueUpgradeNodeMessage>(OnUpgradeNode);
            subs.Event<CMUQueueProjectMessage>(OnProject);
            subs.Event<CMUQueueSubmitMessage>(OnSubmit);
            subs.Event<CMUQueueNewContractMessage>(OnNewContract);
            subs.Event<CMUQueueResetUpgradesMessage>(OnResetUpgrades);
        });
    }

    private void OnMapInit(Entity<CMUQueueNetworkConsoleComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Status = Loc.GetString("cmu-queue-status-idle");
        UpdateUi(ent);
    }

    private void OnUiOpened(Entity<CMUQueueNetworkConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnNewContract(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueNewContractMessage args)
    {
        var comp = ent.Comp;
        if (comp.Scenarios.Count == 0)
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-scenarios");
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        var scenarioId = comp.CompletedContracts == 0 && comp.Scenarios.Any(id => id.Id == "CMUQueueNetworkTutorial")
            ? new ProtoId<CMUQueueNetworkPrototype>("CMUQueueNetworkTutorial")
            : comp.Scenarios[_random.Next(comp.Scenarios.Count)];
        var proto = _prototypes.Index(scenarioId);

        comp.ActiveScenario = scenarioId;
        comp.Active = true;
        comp.Completed = false;
        comp.Failed = false;
        comp.Budget = proto.Budget;
        comp.InitialBudget = proto.Budget;
        comp.TimeLeft = proto.DiagnosticMinutes;
        comp.InitialTime = proto.DiagnosticMinutes;
        comp.Reward = proto.Reward;
        comp.Elapsed = 0;
        comp.Status = Loc.GetString(
            "cmu-queue-status-started",
            ("network", LocOrId(proto.Name, proto.ID)));
        comp.StatusKind = CMUQueueStatusKind.Neutral;

        comp.Nodes.Clear();
        comp.Analyses.Clear();
        comp.History.Clear();

        foreach (var node in proto.Nodes)
        {
            comp.Nodes[node.Id] = new CMUQueueNodeRuntime
            {
                ServiceRate = node.ServiceRate,
                Queue = node.InitialQueue,
            };
        }

        AddHistoryPoint(comp, 0, CurrentQueues(comp, proto));
        UpdateUi(ent);
    }

    private void OnAnalyzeNode(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueAnalyzeNodeMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var proto))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        var nodeId = args.NodeId;
        var node = proto.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null || !comp.Nodes.TryGetValue(nodeId, out var runtime))
        {
            comp.Status = Loc.GetString("cmu-queue-status-unknown-node");
            UpdateUi(ent);
            return;
        }

        if (comp.Analyses.ContainsKey(nodeId))
        {
            comp.Status = Loc.GetString(
                "cmu-queue-status-already-analyzed",
                ("node", NodeName(proto, nodeId)));
            UpdateUi(ent);
            return;
        }

        if (comp.TimeLeft < proto.AnalysisCost)
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-time");
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        if (!CMUQueueNetworkMath.TrySolveArrivalRates(proto, out var rates, out var error))
        {
            comp.Status = Loc.GetString("cmu-queue-status-error", ("error", error));
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        var lambda = rates[nodeId];
        var metrics = CMUQueueNetworkMath.ComputeMetrics(lambda, runtime.ServiceRate);
        comp.Analyses[nodeId] = new CMUQueueAnalysis
        {
            Lambda = lambda,
            Metrics = metrics,
        };

        comp.TimeLeft -= proto.AnalysisCost;
        comp.Elapsed += proto.AnalysisCost;
        AddHistoryPoint(comp, comp.Elapsed, CurrentQueues(comp, proto));

        comp.Status = Loc.GetString(
            "cmu-queue-status-analyzed",
            ("node", NodeName(proto, nodeId)),
            ("rho", metrics.Rho.ToString("0.00", CultureInfo.InvariantCulture)));
        comp.StatusKind = metrics.Stable ? CMUQueueStatusKind.Good : CMUQueueStatusKind.Bad;
        UpdateUi(ent);
    }

    private void OnUpgradeNode(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueUpgradeNodeMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var proto))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        var nodeId = args.NodeId;
        var node = proto.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null || !comp.Nodes.TryGetValue(nodeId, out var runtime))
        {
            comp.Status = Loc.GetString("cmu-queue-status-unknown-node");
            UpdateUi(ent);
            return;
        }

        if (runtime.Upgrades >= node.MaxUpgrades)
        {
            comp.Status = Loc.GetString(
                "cmu-queue-status-max-upgrades",
                ("node", NodeName(proto, nodeId)));
            UpdateUi(ent);
            return;
        }

        if (comp.Budget < node.UpgradeCost)
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-budget");
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        comp.Budget -= node.UpgradeCost;
        runtime.Upgrades++;
        runtime.ServiceRate *= node.UpgradeMultiplier;
        RecomputeAnalyses(comp);

        comp.Status = Loc.GetString(
            "cmu-queue-status-upgraded",
            ("node", NodeName(proto, nodeId)),
            ("rate", runtime.ServiceRate.ToString("0.##", CultureInfo.InvariantCulture)));
        comp.StatusKind = CMUQueueStatusKind.Good;
        UpdateUi(ent);
    }

    private void OnResetUpgrades(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueResetUpgradesMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var proto))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        var refund = 0;
        foreach (var node in proto.Nodes)
        {
            if (!comp.Nodes.TryGetValue(node.Id, out var runtime))
                continue;

            refund += node.UpgradeCost * runtime.Upgrades;
            runtime.ServiceRate = node.ServiceRate;
            runtime.Upgrades = 0;
        }

        comp.Budget = Math.Min(comp.InitialBudget, comp.Budget + refund);
        RecomputeAnalyses(comp);
        comp.Status = refund > 0
            ? Loc.GetString("cmu-queue-status-reset", ("refund", refund))
            : Loc.GetString("cmu-queue-status-reset-none");
        UpdateUi(ent);
    }

    private void OnProject(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueProjectMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var proto))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        if (comp.TimeLeft < proto.ProjectionCost)
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-time");
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        if (!CMUQueueNetworkMath.TrySolveArrivalRates(proto, out var rates, out var error))
        {
            comp.Status = Loc.GetString("cmu-queue-status-error", ("error", error));
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        var serviceRates = new Dictionary<string, double>(StringComparer.Ordinal);
        var startQueues = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var node in proto.Nodes)
        {
            if (!comp.Nodes.TryGetValue(node.Id, out var runtime))
                continue;

            serviceRates[node.Id] = runtime.ServiceRate;
            startQueues[node.Id] = runtime.Queue;
        }

        var baseMinute = comp.Elapsed;
        var points = CMUQueueNetworkMath.Project(
            proto,
            serviceRates,
            rates,
            startQueues,
            proto.ProjectionMinutes);

        foreach (var point in points)
        {
            if (point.Minute == 0)
                continue;

            AddHistoryPoint(comp, baseMinute + point.Minute, point.Queues);
        }

        var final = points[^1];
        foreach (var node in proto.Nodes)
        {
            if (comp.Nodes.TryGetValue(node.Id, out var runtime) &&
                final.Queues.TryGetValue(node.Id, out var queue))
            {
                runtime.Queue = queue;
            }
        }

        comp.Elapsed += proto.ProjectionMinutes;
        comp.TimeLeft -= proto.ProjectionCost;
        comp.Status = Loc.GetString(
            "cmu-queue-status-projected",
            ("minutes", proto.ProjectionMinutes));
        UpdateUi(ent);
    }

    private void OnSubmit(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueSubmitMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var proto))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        if (!CMUQueueNetworkMath.TrySolveArrivalRates(proto, out var rates, out var error))
        {
            comp.Status = Loc.GetString("cmu-queue-status-error", ("error", error));
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        var unstable = new List<string>();
        foreach (var node in proto.Nodes)
        {
            if (!comp.Nodes.TryGetValue(node.Id, out var runtime))
                continue;

            var metrics = CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], runtime.ServiceRate);
            if (!metrics.Stable)
                unstable.Add(NodeName(proto, node.Id));
        }

        if (unstable.Count == 0)
        {
            _corporateConsole.AddToCorporateBudget(comp.Reward);
            comp.Completed = true;
            comp.Failed = false;
            comp.Active = false;
            comp.CompletedContracts++;
            comp.Status = Loc.GetString(
                "cmu-queue-status-complete",
                ("reward", comp.Reward));
            comp.StatusKind = CMUQueueStatusKind.Good;
        }
        else
        {
            comp.Status = Loc.GetString(
                "cmu-queue-status-unstable",
                ("nodes", string.Join(", ", unstable)));
            comp.StatusKind = CMUQueueStatusKind.Bad;
        }

        UpdateUi(ent);
    }

    private bool TryGetActive(
        Entity<CMUQueueNetworkConsoleComponent> ent,
        out CMUQueueNetworkPrototype proto)
    {
        proto = default!;
        if (!ent.Comp.Active || ent.Comp.ActiveScenario is not { } scenarioId)
            return false;

        proto = _prototypes.Index(scenarioId);
        return true;
    }

    private static void RecomputeAnalyses(CMUQueueNetworkConsoleComponent comp)
    {
        foreach (var (nodeId, analysis) in comp.Analyses)
        {
            if (!comp.Nodes.TryGetValue(nodeId, out var runtime))
                continue;

            analysis.Metrics = CMUQueueNetworkMath.ComputeMetrics(analysis.Lambda, runtime.ServiceRate);
        }
    }

    private static Dictionary<string, double> CurrentQueues(
        CMUQueueNetworkConsoleComponent comp,
        CMUQueueNetworkPrototype proto)
    {
        var queues = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var node in proto.Nodes)
        {
            queues[node.Id] = comp.Nodes.TryGetValue(node.Id, out var runtime)
                ? runtime.Queue
                : 0;
        }

        return queues;
    }

    private static void AddHistoryPoint(
        CMUQueueNetworkConsoleComponent comp,
        int minute,
        IReadOnlyDictionary<string, double> queues)
    {
        var existing = comp.History.FirstOrDefault(point => point.Minute == minute);
        if (existing != null)
        {
            existing.Queues = new Dictionary<string, double>(queues, StringComparer.Ordinal);
            return;
        }

        comp.History.Add(new CMUQueueHistoryPoint
        {
            Minute = minute,
            Queues = new Dictionary<string, double>(queues, StringComparer.Ordinal),
        });
        comp.History.Sort((left, right) => left.Minute.CompareTo(right.Minute));
    }

    private string NodeName(CMUQueueNetworkPrototype proto, string nodeId)
    {
        var node = proto.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
        if (node == null)
            return nodeId;

        return LocOrId(node.Name, node.Id);
    }

    private string LocOrId(string key, string fallback)
    {
        return string.IsNullOrEmpty(key) ? fallback : Loc.GetString(key);
    }

    private void UpdateUi(Entity<CMUQueueNetworkConsoleComponent> ent)
    {
        CMUQueueNetworkPrototype? proto = null;
        if (ent.Comp.ActiveScenario is { } scenarioId)
            proto = _prototypes.Index(scenarioId);

        var nodes = new List<CMUQueueNodeInfo>();
        var routes = new List<CMUQueueRouteInfo>();
        var history = new List<CMUQueueHistoryInfo>();

        if (proto != null)
        {
            foreach (var node in proto.Nodes)
            {
                ent.Comp.Nodes.TryGetValue(node.Id, out var runtime);
                ent.Comp.Analyses.TryGetValue(node.Id, out var analysis);
                var metrics = analysis?.Metrics ?? default;

                nodes.Add(new CMUQueueNodeInfo(
                    node.Id,
                    node.Name,
                    runtime?.ServiceRate ?? node.ServiceRate,
                    proto.Sources.Where(source => source.Node == node.Id).Sum(source => source.Rate),
                    runtime?.Queue ?? 0,
                    runtime?.Upgrades ?? 0,
                    node.MaxUpgrades,
                    node.UpgradeCost,
                    node.UpgradeMultiplier,
                    node.Column,
                    node.Row,
                    analysis != null,
                    analysis?.Lambda ?? 0,
                    metrics.Rho,
                    metrics.L,
                    metrics.Lq,
                    metrics.W,
                    metrics.Wq,
                    metrics.Stable));
            }

            foreach (var route in proto.Routes)
                routes.Add(new CMUQueueRouteInfo(route.From, route.To, route.Probability));

            foreach (var point in ent.Comp.History)
            {
                var queues = new List<float>(proto.Nodes.Count);
                foreach (var node in proto.Nodes)
                {
                    queues.Add((float)(point.Queues.TryGetValue(node.Id, out var queue)
                        ? queue
                        : 0));
                }

                history.Add(new CMUQueueHistoryInfo(point.Minute, queues));
            }
        }

        _ui.SetUiState(
            ent.Owner,
            CMUQueueNetworkConsoleUiKey.Key,
            new CMUQueueNetworkConsoleState(
                ent.Comp.Active,
                ent.Comp.Completed,
                ent.Comp.Failed,
                ent.Comp.Status,
                ent.Comp.StatusKind,
                proto?.Name ?? string.Empty,
                proto?.Description ?? string.Empty,
                ent.Comp.Budget,
                ent.Comp.TimeLeft,
                ent.Comp.InitialTime,
                ent.Comp.Reward,
                ent.Comp.Elapsed,
                proto?.AnalysisCost ?? 1,
                proto?.ProjectionCost ?? 3,
                proto?.ProjectionMinutes ?? 3,
                nodes,
                routes,
                history));
    }
}
