using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Server.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Runs generated production diagnostic contracts. The server owns the
/// network state, the maintenance budget and the diagnostic clock; the client
/// only renders state and sends intents.
/// </summary>
public sealed partial class CMUQueueNetworkConsoleSystem : EntitySystem
{
    [Dependency] private CorporateConsoleSystem _corporateConsole = default!;
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
            subs.Event<CMUQueueRerouteMessage>(OnReroute);
            subs.Event<CMUQueueProjectMessage>(OnProject);
            subs.Event<CMUQueueSubmitMessage>(OnSubmit);
            subs.Event<CMUQueueNewContractMessage>(OnNewContract);
            subs.Event<CMUQueueResetInterventionsMessage>(OnResetInterventions);
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
        var tutorial = comp.CompletedContracts == 0 && comp.ForcedTopology == null;
        var forced = comp.ForcedTopology ?? (tutorial ? CMUQueueTopologyKind.Line : null);
        var seed = comp.ForcedSeed ?? _random.Next();
        var network = CMUQueueNetworkGenerator.Generate(forced, seed, tutorial);

        comp.Network = network;
        comp.LastSeed = network.Seed;
        comp.Active = true;
        comp.Completed = false;
        comp.Budget = network.Budget;
        comp.InitialBudget = network.Budget;
        comp.TimeLeft = network.DiagnosticMinutes;
        comp.InitialTime = network.DiagnosticMinutes;
        comp.Reward = network.Reward;
        comp.Elapsed = 0;
        comp.Status = Loc.GetString(
            "cmu-queue-status-started",
            ("network", LocOrId(network.Name, Loc.GetString("cmu-queue-network-unknown"))));
        comp.StatusKind = CMUQueueStatusKind.Neutral;

        comp.Nodes.Clear();
        comp.Analyses.Clear();
        comp.BypassFractions.Clear();
        comp.History.Clear();

        foreach (var node in network.Nodes)
        {
            comp.Nodes[node.Id] = new CMUQueueNodeRuntime
            {
                ServiceRate = node.ServiceRate,
                Queue = node.InitialQueue,
            };
        }

        foreach (var bypass in network.Bypasses)
            comp.BypassFractions[bypass.Id] = 0;

        AddHistoryPoint(comp, 0, CurrentQueues(comp, network));
        UpdateUi(ent);
    }

    private void OnAnalyzeNode(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueAnalyzeNodeMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var network))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        var nodeId = args.NodeId;
        var node = network.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
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
                ("node", NodeName(network, nodeId)));
            UpdateUi(ent);
            return;
        }

        if (comp.TimeLeft < network.AnalysisCost)
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-time");
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        if (!CMUQueueNetworkMath.TrySolveArrivalRates(
                network,
                comp.BypassFractions,
                out var rates,
                out var error))
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

        comp.TimeLeft -= network.AnalysisCost;
        comp.Elapsed += network.AnalysisCost;
        AddHistoryPoint(comp, comp.Elapsed, CurrentQueues(comp, network));

        comp.Status = Loc.GetString(
            "cmu-queue-status-analyzed",
            ("node", NodeName(network, nodeId)),
            ("rho", metrics.Rho.ToString("0.00", CultureInfo.CurrentCulture)));
        comp.StatusKind = metrics.Stable ? CMUQueueStatusKind.Good : CMUQueueStatusKind.Bad;
        UpdateUi(ent);
    }

    private void OnUpgradeNode(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueUpgradeNodeMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var network))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        var nodeId = args.NodeId;
        var node = network.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
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
                ("node", NodeName(network, nodeId)));
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
        RecomputeAnalyses(comp, network);

        comp.Status = Loc.GetString(
            "cmu-queue-status-upgraded",
            ("node", NodeName(network, nodeId)),
            ("rate", runtime.ServiceRate.ToString("0.##", CultureInfo.CurrentCulture)));
        comp.StatusKind = CMUQueueStatusKind.Good;
        UpdateUi(ent);
    }

    private void OnReroute(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueRerouteMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var network))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        var bypassId = args.BypassId;
        var bypass = network.Bypasses.FirstOrDefault(candidate => candidate.Id == bypassId);
        if (bypass == null)
        {
            comp.Status = Loc.GetString("cmu-queue-status-unknown-bypass");
            UpdateUi(ent);
            return;
        }

        var current = comp.BypassFractions.GetValueOrDefault(bypass.Id);
        if (current + bypass.StepFraction > bypass.MaxFraction + 0.001)
        {
            comp.Status = Loc.GetString(
                "cmu-queue-status-reroute-max",
                ("from", NodeName(network, bypass.From)),
                ("to", NodeName(network, bypass.To)));
            UpdateUi(ent);
            return;
        }

        if (comp.Budget < bypass.CostPerStep)
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-budget");
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        comp.Budget -= bypass.CostPerStep;
        comp.BypassFractions[bypass.Id] = current + bypass.StepFraction;
        RecomputeAnalyses(comp, network);

        comp.Status = Loc.GetString(
            "cmu-queue-status-rerouted",
            ("from", NodeName(network, bypass.From)),
            ("to", NodeName(network, bypass.To)),
            ("percent", ((current + bypass.StepFraction) * 100).ToString("0", CultureInfo.CurrentCulture)));
        comp.StatusKind = CMUQueueStatusKind.Neutral;
        UpdateUi(ent);
    }

    private void OnResetInterventions(
        Entity<CMUQueueNetworkConsoleComponent> ent,
        ref CMUQueueResetInterventionsMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var network))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        var refund = 0;
        foreach (var node in network.Nodes)
        {
            if (!comp.Nodes.TryGetValue(node.Id, out var runtime))
                continue;

            refund += node.UpgradeCost * runtime.Upgrades;
            runtime.ServiceRate = node.ServiceRate;
            runtime.Upgrades = 0;
        }

        foreach (var bypass in network.Bypasses)
        {
            var current = comp.BypassFractions.GetValueOrDefault(bypass.Id);
            var steps = bypass.StepFraction <= 0
                ? 0
                : (int) Math.Round(current / bypass.StepFraction);
            refund += steps * bypass.CostPerStep;
            comp.BypassFractions[bypass.Id] = 0;
        }

        comp.Budget = Math.Min(comp.InitialBudget, comp.Budget + refund);
        RecomputeAnalyses(comp, network);
        comp.Status = refund > 0
            ? Loc.GetString("cmu-queue-status-reset", ("refund", refund))
            : Loc.GetString("cmu-queue-status-reset-none");
        comp.StatusKind = CMUQueueStatusKind.Neutral;
        UpdateUi(ent);
    }

    private void OnProject(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueProjectMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var network))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        if (comp.TimeLeft < network.ProjectionCost)
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-time");
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        if (!CMUQueueNetworkMath.TrySolveArrivalRates(
                network,
                comp.BypassFractions,
                out var rates,
                out var error))
        {
            comp.Status = Loc.GetString("cmu-queue-status-error", ("error", error));
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        var serviceRates = new Dictionary<string, double>(StringComparer.Ordinal);
        var startQueues = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var node in network.Nodes)
        {
            if (!comp.Nodes.TryGetValue(node.Id, out var runtime))
                continue;

            serviceRates[node.Id] = runtime.ServiceRate;
            startQueues[node.Id] = runtime.Queue;
        }

        var baseMinute = comp.Elapsed;
        var points = CMUQueueNetworkMath.Project(
            network,
            serviceRates,
            rates,
            startQueues,
            network.ProjectionMinutes);

        foreach (var point in points)
        {
            if (point.Minute == 0)
                continue;

            AddHistoryPoint(comp, baseMinute + point.Minute, point.Queues);
        }

        var final = points[^1];
        foreach (var node in network.Nodes)
        {
            if (comp.Nodes.TryGetValue(node.Id, out var runtime) &&
                final.Queues.TryGetValue(node.Id, out var queue))
            {
                runtime.Queue = queue;
            }
        }

        comp.Elapsed += network.ProjectionMinutes;
        comp.TimeLeft -= network.ProjectionCost;
        comp.Status = Loc.GetString(
            "cmu-queue-status-projected",
            ("minutes", network.ProjectionMinutes));
        comp.StatusKind = CMUQueueStatusKind.Neutral;
        UpdateUi(ent);
    }

    private void OnSubmit(Entity<CMUQueueNetworkConsoleComponent> ent, ref CMUQueueSubmitMessage args)
    {
        var comp = ent.Comp;
        if (!TryGetActive(ent, out var network))
        {
            comp.Status = Loc.GetString("cmu-queue-status-no-contract");
            UpdateUi(ent);
            return;
        }

        if (!CMUQueueNetworkMath.TrySolveArrivalRates(
                network,
                comp.BypassFractions,
                out var rates,
                out var error))
        {
            comp.Status = Loc.GetString("cmu-queue-status-error", ("error", error));
            comp.StatusKind = CMUQueueStatusKind.Bad;
            UpdateUi(ent);
            return;
        }

        var unstable = new List<string>();
        foreach (var node in network.Nodes)
        {
            if (!comp.Nodes.TryGetValue(node.Id, out var runtime))
                continue;

            var metrics = CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], runtime.ServiceRate);
            if (!metrics.Stable)
                unstable.Add(NodeName(network, node.Id));
        }

        if (unstable.Count == 0)
        {
            _corporateConsole.AddToCorporateBudget(comp.Reward);
            comp.Completed = true;
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
        out CMUQueueNetworkDefinition network)
    {
        if (!ent.Comp.Active || ent.Comp.Network == null)
        {
            network = default!;
            return false;
        }

        network = ent.Comp.Network;
        return true;
    }

    private void RecomputeAnalyses(
        CMUQueueNetworkConsoleComponent comp,
        CMUQueueNetworkDefinition network)
    {
        if (!CMUQueueNetworkMath.TrySolveArrivalRates(
                network,
                comp.BypassFractions,
                out var rates,
                out _))
        {
            return;
        }

        foreach (var (nodeId, analysis) in comp.Analyses)
        {
            if (!comp.Nodes.TryGetValue(nodeId, out var runtime) ||
                !rates.TryGetValue(nodeId, out var lambda))
            {
                continue;
            }

            analysis.Lambda = lambda;
            analysis.Metrics = CMUQueueNetworkMath.ComputeMetrics(lambda, runtime.ServiceRate);
        }
    }

    private static Dictionary<string, double> CurrentQueues(
        CMUQueueNetworkConsoleComponent comp,
        CMUQueueNetworkDefinition network)
    {
        var queues = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var node in network.Nodes)
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

    private string NodeName(CMUQueueNetworkDefinition network, string nodeId)
    {
        var node = network.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
        if (node == null)
            return nodeId;

        var name = LocOrId(node.Name, node.Id);
        return string.IsNullOrEmpty(node.Suffix) ? name : $"{name} {node.Suffix}";
    }

    private string LocOrId(string key, string fallback)
    {
        if (string.IsNullOrEmpty(key))
            return fallback;

        return Loc.TryGetString(key, out var value) ? value : fallback;
    }

    private void UpdateUi(Entity<CMUQueueNetworkConsoleComponent> ent)
    {
        var comp = ent.Comp;
        var network = comp.Network;
        var nodes = new List<CMUQueueNodeInfo>();
        var routes = new List<CMUQueueRouteInfo>();
        var bypasses = new List<CMUQueueBypassInfo>();
        var history = new List<CMUQueueHistoryInfo>();

        if (network != null)
        {
            var effectiveSources = CMUQueueNetworkMath.GetEffectiveSourceRates(
                network,
                comp.BypassFractions);

            foreach (var node in network.Nodes)
            {
                comp.Nodes.TryGetValue(node.Id, out var runtime);
                comp.Analyses.TryGetValue(node.Id, out var analysis);
                var metrics = analysis?.Metrics ?? default;

                nodes.Add(new CMUQueueNodeInfo(
                    node.Id,
                    node.Name,
                    node.Suffix,
                    runtime?.ServiceRate ?? node.ServiceRate,
                    effectiveSources.GetValueOrDefault(node.Id),
                    runtime?.Queue ?? 0,
                    runtime?.Upgrades ?? 0,
                    node.MaxUpgrades,
                    node.UpgradeCost,
                    node.UpgradeMultiplier,
                    analysis != null,
                    analysis?.Lambda ?? 0,
                    metrics.Rho,
                    metrics.L,
                    metrics.Lq,
                    metrics.W,
                    metrics.Wq,
                    metrics.Stable));
            }

            foreach (var route in network.Routes)
                routes.Add(new CMUQueueRouteInfo(route.From, route.To, route.Probability));

            foreach (var bypass in network.Bypasses)
            {
                var current = comp.BypassFractions.GetValueOrDefault(bypass.Id);
                var maxed = current + bypass.StepFraction > bypass.MaxFraction + 0.001;
                bypasses.Add(new CMUQueueBypassInfo(
                    bypass.Id,
                    bypass.From,
                    bypass.To,
                    current,
                    bypass.MaxFraction,
                    bypass.StepFraction,
                    bypass.CostPerStep,
                    !maxed && comp.Budget >= bypass.CostPerStep,
                    maxed));
            }

            foreach (var point in comp.History)
            {
                var queues = new List<float>(network.Nodes.Count);
                foreach (var node in network.Nodes)
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
                comp.Active,
                comp.Completed,
                comp.Status,
                comp.StatusKind,
                network?.Name ?? string.Empty,
                network?.Description ?? string.Empty,
                comp.Budget,
                comp.TimeLeft,
                comp.InitialTime,
                comp.Reward,
                comp.Elapsed,
                network?.AnalysisCost ?? 1,
                network?.ProjectionCost ?? 3,
                network?.ProjectionMinutes ?? 3,
                nodes,
                routes,
                bypasses,
                history));
    }
}
