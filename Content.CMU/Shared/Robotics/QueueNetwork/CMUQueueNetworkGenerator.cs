using System;
using System.Collections.Generic;
using System.Linq;
using SystemRandom = System.Random;

namespace Content.Shared.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Builds a fresh queueing network for each diagnostic contract. The pool
/// supplies topology templates; every contract randomizes loads, service
/// rates, costs, budget, reward and, where the topology supports it, an
/// intake-diversion bypass.
/// </summary>
public static class CMUQueueNetworkGenerator
{
    private const int MaxAttempts = 32;

    private static readonly CMUQueueTopologyKind[] Pool =
    {
        CMUQueueTopologyKind.Line,
        CMUQueueTopologyKind.Rework,
        CMUQueueTopologyKind.Branch,
        CMUQueueTopologyKind.SplitMerge,
        CMUQueueTopologyKind.Line,
        CMUQueueTopologyKind.Rework,
    };

    private static readonly int[] UpgradeCosts = { 150, 200, 250, 300 };

    public static CMUQueueNetworkDefinition Generate(
        CMUQueueTopologyKind? forced = null,
        int? seed = null,
        bool tutorial = false)
    {
        var baseSeed = seed ?? SystemRandom.Shared.Next();

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var actualSeed = unchecked(baseSeed + attempt * 7919);
            var random = new SystemRandom(actualSeed);
            var topology = tutorial
                ? CMUQueueTopologyKind.Line
                : forced ?? Pool[random.Next(Pool.Length)];

            var network = Build(topology, random, tutorial);
            network.Seed = actualSeed;
            network.Topology = topology;

            if (FinalizeNetwork(network, random, tutorial))
                return network;
        }

        return BuildFallback(baseSeed);
    }

    private static CMUQueueNetworkDefinition Build(
        CMUQueueTopologyKind topology,
        SystemRandom random,
        bool tutorial)
    {
        return topology switch
        {
            CMUQueueTopologyKind.Branch => BuildBranch(random),
            CMUQueueTopologyKind.Rework => BuildRework(random),
            CMUQueueTopologyKind.SplitMerge => BuildSplitMerge(random),
            _ => BuildLine(random, tutorial),
        };
    }

    private static CMUQueueNetworkDefinition BuildLine(SystemRandom random, bool tutorial)
    {
        var network = NewNetwork(
            tutorial ? "cmu-queue-network-tutorial-name" : "cmu-queue-network-line-name",
            tutorial ? "cmu-queue-network-tutorial-desc" : "cmu-queue-network-line-desc");

        AddNode(network, "intake", "cmu-queue-node-intake");
        var processors = tutorial ? 1 : 1 + random.Next(0, 2);
        for (var index = 0; index < processors; index++)
        {
            AddNode(
                network,
                $"processor{index + 1}",
                "cmu-queue-node-processor",
                processors > 1 ? $"{(char) ('A' + index)}" : string.Empty);
        }

        AddNode(network, "packaging", "cmu-queue-node-packaging");
        AddSource(network, "intake", tutorial ? 9 : 8 + random.Next(0, 5));

        AddRoute(network, "intake", "processor1", 1);
        for (var index = 1; index < processors; index++)
            AddRoute(network, $"processor{index}", $"processor{index + 1}", 1);

        AddRoute(network, processors == 1 ? "processor1" : $"processor{processors}", "packaging", 1);
        AddRoute(network, "packaging", "sink", 1);

        for (var index = 0; index < network.Nodes.Count; index++)
        {
            network.Nodes[index].Column = index;
            network.Nodes[index].Row = 0;
        }

        return network;
    }

    private static CMUQueueNetworkDefinition BuildRework(SystemRandom random)
    {
        var network = NewNetwork(
            "cmu-queue-network-rework-name",
            "cmu-queue-network-rework-desc");

        AddNode(network, "intake", "cmu-queue-node-intake");
        AddNode(network, "processor", "cmu-queue-node-processor");
        AddNode(network, "assembler", "cmu-queue-node-assembler");
        AddNode(network, "qc", "cmu-queue-node-qc");
        AddNode(network, "packaging", "cmu-queue-node-packaging");

        AddSource(network, "intake", 10 + random.Next(0, 5));

        var rework = 0.05 + random.NextDouble() * 0.1;
        AddRoute(network, "intake", "processor", 1);
        AddRoute(network, "processor", "assembler", 1);
        AddRoute(network, "assembler", "qc", 1 - rework);
        AddRoute(network, "assembler", "processor", rework);
        AddRoute(network, "qc", "packaging", 1);
        AddRoute(network, "packaging", "sink", 1);

        SetPosition(network, "intake", 0, 1);
        SetPosition(network, "processor", 1, 0);
        SetPosition(network, "assembler", 2, 0);
        SetPosition(network, "qc", 3, 0);
        SetPosition(network, "packaging", 4, 1);

        return network;
    }

    private static CMUQueueNetworkDefinition BuildBranch(SystemRandom random)
    {
        var network = NewNetwork(
            "cmu-queue-network-branch-name",
            "cmu-queue-network-branch-desc");

        AddNode(network, "intake", "cmu-queue-node-intake");
        AddNode(network, "processorA", "cmu-queue-node-processor", "A");
        AddNode(network, "processorB", "cmu-queue-node-processor", "B");
        AddNode(network, "merge", "cmu-queue-node-merge");
        AddNode(network, "packaging", "cmu-queue-node-packaging");

        AddSource(network, "intake", 11 + random.Next(0, 5));

        var split = 0.55 + random.NextDouble() * 0.15;
        AddRoute(network, "intake", "processorA", split);
        AddRoute(network, "intake", "processorB", 1 - split);
        AddRoute(network, "processorA", "merge", 1);
        AddRoute(network, "processorB", "merge", 1);
        AddRoute(network, "merge", "packaging", 1);
        AddRoute(network, "packaging", "sink", 1);

        SetPosition(network, "intake", 0, 1);
        SetPosition(network, "processorA", 1, 0);
        SetPosition(network, "processorB", 1, 2);
        SetPosition(network, "merge", 2, 1);
        SetPosition(network, "packaging", 3, 1);

        AddBypass(network, "intake", "processorB");

        return network;
    }

    private static CMUQueueNetworkDefinition BuildSplitMerge(SystemRandom random)
    {
        var network = NewNetwork(
            "cmu-queue-network-split-name",
            "cmu-queue-network-split-desc");

        AddNode(network, "intake", "cmu-queue-node-intake");
        AddNode(network, "lineA1", "cmu-queue-node-processor", "A1");
        AddNode(network, "lineA2", "cmu-queue-node-processor", "A2");
        AddNode(network, "lineB1", "cmu-queue-node-processor", "B1");
        AddNode(network, "lineB2", "cmu-queue-node-processor", "B2");
        AddNode(network, "merge", "cmu-queue-node-merge");

        AddSource(network, "intake", 12 + random.Next(0, 6));

        var split = 0.45 + random.NextDouble() * 0.1;
        AddRoute(network, "intake", "lineA1", split);
        AddRoute(network, "intake", "lineB1", 1 - split);
        AddRoute(network, "lineA1", "lineA2", 1);
        AddRoute(network, "lineB1", "lineB2", 1);
        AddRoute(network, "lineA2", "merge", 1);
        AddRoute(network, "lineB2", "merge", 1);
        AddRoute(network, "merge", "sink", 1);

        SetPosition(network, "intake", 0, 1);
        SetPosition(network, "lineA1", 1, 0);
        SetPosition(network, "lineA2", 2, 0);
        SetPosition(network, "lineB1", 1, 2);
        SetPosition(network, "lineB2", 2, 2);
        SetPosition(network, "merge", 3, 1);

        AddBypass(network, "intake", "lineB1");

        return network;
    }

    private static bool FinalizeNetwork(
        CMUQueueNetworkDefinition network,
        SystemRandom random,
        bool tutorial)
    {
        if (!CMUQueueNetworkMath.TrySolveArrivalRates(network, out var rates, out _))
            return false;

        if (network.Nodes.Any(node => !rates.TryGetValue(node.Id, out var rate) || rate <= 0))
            return false;

        var bottleneckCount = tutorial
            ? 1
            : network.Topology switch
            {
                CMUQueueTopologyKind.Rework => 2,
                CMUQueueTopologyKind.Branch => 1 + random.Next(0, 2),
                CMUQueueTopologyKind.SplitMerge => 1 + random.Next(0, 2),
                _ => 1,
            };

        var candidates = network.Nodes.ToList();
        Shuffle(candidates, random);
        var bottleneckIds = candidates
            .Take(bottleneckCount)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var stableNodes = 0;
        foreach (var node in network.Nodes)
        {
            var lambda = rates[node.Id];
            var isBottleneck = bottleneckIds.Contains(node.Id);
            var targetRho = isBottleneck
                ? 1.12 + random.NextDouble() * 0.33
                : 0.65 + random.NextDouble() * 0.3;
            node.ServiceRate = Math.Max(0.1, Math.Round(lambda / targetRho, 2));
            node.UpgradeMultiplier = 1.55 + random.NextDouble() * 0.3;
            node.UpgradeCost = PickUpgradeCost(random);
            node.MaxUpgrades = 1;

            if (!isBottleneck)
                stableNodes++;
        }

        // Keep at least one healthy node under heavy but stable load so the
        // player has a plausible wrong suspect to rule out.
        EnsureRedHerring(network, rates, bottleneckIds, random);

        var minimumCost = network.Nodes
            .Where(node => bottleneckIds.Contains(node.Id))
            .Sum(node => GetMinimumStableCost(node, rates[node.Id]));

        network.Budget = RoundTo50(
            Math.Max(300, minimumCost * (1.1 + random.NextDouble() * 0.25)));
        network.Reward = RoundTo50(
            Math.Max(700, 500 + minimumCost + random.Next(0, 300)));
        network.DiagnosticMinutes = 15;
        network.AnalysisCost = 1;
        network.ProjectionCost = 3;
        network.ProjectionMinutes = 3;

        if (stableNodes == 0)
            return false;

        if (network.Budget < minimumCost + 50)
            return false;

        foreach (var node in network.Nodes)
        {
            var lambda = rates[node.Id];
            var isBottleneck = bottleneckIds.Contains(node.Id);
            var metrics = CMUQueueNetworkMath.ComputeMetrics(lambda, node.ServiceRate);
            if (isBottleneck && metrics.Stable)
                return false;

            var maxRate = node.ServiceRate * Math.Pow(node.UpgradeMultiplier, node.MaxUpgrades);
            var upgraded = CMUQueueNetworkMath.ComputeMetrics(lambda, maxRate);
            if (isBottleneck && !upgraded.Stable)
                return false;
        }

        foreach (var bypass in network.Bypasses)
        {
            if (!rates.ContainsKey(bypass.From) || !rates.ContainsKey(bypass.To))
                return false;

            var source = network.Sources.FirstOrDefault(source => source.Node == bypass.From);
            if (source == null || source.Rate <= 0)
                return false;
        }

        return true;
    }

    private static void EnsureRedHerring(
        CMUQueueNetworkDefinition network,
        IReadOnlyDictionary<string, double> rates,
        HashSet<string> bottlenecks,
        SystemRandom random)
    {
        var stable = network.Nodes
            .Where(node => !bottlenecks.Contains(node.Id))
            .ToList();

        if (stable.Count == 0)
            return;

        if (stable.Any(node =>
                CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], node.ServiceRate).Rho >= 0.75))
            return;

        var candidate = stable[random.Next(stable.Count)];
        candidate.ServiceRate = Math.Max(
            0.1,
            Math.Round(rates[candidate.Id] / (0.8 + random.NextDouble() * 0.1), 2));
    }

    private static int GetMinimumStableCost(CMUQueueNodeDefinition node, double lambda)
    {
        var maxRate = node.ServiceRate * Math.Pow(node.UpgradeMultiplier, node.MaxUpgrades);
        if (maxRate <= node.ServiceRate + 0.001)
            return node.UpgradeCost;

        var target = Math.Min(maxRate, lambda * 1.02);
        var costPerRate = node.UpgradeCost / (maxRate - node.ServiceRate);
        return Math.Max(
            1,
            (int) Math.Ceiling((target - node.ServiceRate) * costPerRate - 0.0001));
    }

    private static CMUQueueNetworkDefinition BuildFallback(int seed)
    {
        var network = NewNetwork(
            "cmu-queue-network-tutorial-name",
            "cmu-queue-network-tutorial-desc");

        AddNode(network, "intake", "cmu-queue-node-intake");
        AddNode(network, "processor", "cmu-queue-node-processor");
        AddNode(network, "packaging", "cmu-queue-node-packaging");
        AddSource(network, "intake", 9);
        AddRoute(network, "intake", "processor", 1);
        AddRoute(network, "processor", "packaging", 1);
        AddRoute(network, "packaging", "sink", 1);

        SetPosition(network, "intake", 0, 0);
        SetPosition(network, "processor", 1, 0);
        SetPosition(network, "packaging", 2, 0);

        network.Seed = seed;
        network.Topology = CMUQueueTopologyKind.Line;
        network.Nodes[0].ServiceRate = 12;
        network.Nodes[1].ServiceRate = 7;
        network.Nodes[2].ServiceRate = 10;
        foreach (var node in network.Nodes)
        {
            node.UpgradeMultiplier = 1.5;
            node.UpgradeCost = 250;
            node.MaxUpgrades = 1;
        }

        network.Budget = 400;
        network.Reward = 800;
        return network;
    }

    private static CMUQueueNetworkDefinition NewNetwork(string name, string description)
    {
        return new CMUQueueNetworkDefinition
        {
            Name = name,
            Description = description,
        };
    }

    private static void AddNode(
        CMUQueueNetworkDefinition network,
        string id,
        string name,
        string suffix = "")
    {
        network.Nodes.Add(new CMUQueueNodeDefinition
        {
            Id = id,
            Name = name,
            Suffix = suffix,
        });
    }

    private static void SetPosition(
        CMUQueueNetworkDefinition network,
        string id,
        int column,
        int row)
    {
        var node = network.Nodes.FirstOrDefault(candidate => candidate.Id == id);
        if (node == null)
            return;

        node.Column = column;
        node.Row = row;
    }

    private static void AddSource(CMUQueueNetworkDefinition network, string node, double rate)
    {
        network.Sources.Add(new CMUQueueSourceDefinition
        {
            Node = node,
            Rate = rate,
        });
    }

    private static void AddRoute(
        CMUQueueNetworkDefinition network,
        string from,
        string to,
        double probability)
    {
        network.Routes.Add(new CMUQueueRouteDefinition
        {
            From = from,
            To = to,
            Probability = probability,
        });
    }

    private static void AddBypass(
        CMUQueueNetworkDefinition network,
        string from,
        string to)
    {
        network.Bypasses.Add(new CMUQueueBypassDefinition
        {
            Id = $"{from}-{to}",
            From = from,
            To = to,
            MaxFraction = 0.3,
            StepFraction = 0.1,
            CostPerStep = 100,
        });
    }

    private static int PickUpgradeCost(SystemRandom random)
    {
        return UpgradeCosts[random.Next(UpgradeCosts.Length)];
    }

    private static int RoundTo50(double value)
    {
        return (int) (Math.Ceiling(value / 50) * 50);
    }

    private static void Shuffle<T>(IList<T> list, SystemRandom random)
    {
        for (var index = list.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (list[index], list[swap]) = (list[swap], list[index]);
        }
    }
}
