using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using NUnit.Framework;

namespace Content.Tests.CMU14.Robotics;

[TestFixture]
public sealed class CMUQueueNetworkMathTest
{
    [Test]
    public void LineNetworkSolvesArrivalRates()
    {
        var network = MakeNetwork(
            Node("intake", 12),
            Node("processor", 7),
            Node("packaging", 10));
        network.Sources.Add(Source("intake", 9));
        network.Routes.Add(Route("intake", "processor", 1));
        network.Routes.Add(Route("processor", "packaging", 1));
        network.Routes.Add(Route("packaging", "sink", 1));

        Assert.That(CMUQueueNetworkMath.TrySolveArrivalRates(network, out var rates, out var error), Is.True, error);
        Assert.That(rates["intake"], Is.EqualTo(9).Within(0.001));
        Assert.That(rates["processor"], Is.EqualTo(9).Within(0.001));
        Assert.That(rates["packaging"], Is.EqualTo(9).Within(0.001));
    }

    [Test]
    public void ReworkLoopAmplifiesLoad()
    {
        var network = MakeNetwork(
            Node("intake", 15),
            Node("processor", 8),
            Node("assembler", 9),
            Node("qc", 12),
            Node("packaging", 12));
        network.Sources.Add(Source("intake", 10));
        network.Routes.Add(Route("intake", "processor", 1));
        network.Routes.Add(Route("processor", "assembler", 1));
        network.Routes.Add(Route("assembler", "qc", 0.9));
        network.Routes.Add(Route("assembler", "processor", 0.1));
        network.Routes.Add(Route("qc", "packaging", 1));
        network.Routes.Add(Route("packaging", "sink", 1));

        Assert.That(CMUQueueNetworkMath.TrySolveArrivalRates(network, out var rates, out var error), Is.True, error);

        var expected = 10 / 0.9;
        Assert.That(rates["processor"], Is.EqualTo(expected).Within(0.001));
        Assert.That(rates["assembler"], Is.EqualTo(expected).Within(0.001));
        Assert.That(rates["qc"], Is.EqualTo(10).Within(0.001));
        Assert.That(rates["packaging"], Is.EqualTo(10).Within(0.001));
    }

    [Test]
    public void OverloadedNodeHasNegativeSteadyStateValues()
    {
        var metrics = CMUQueueNetworkMath.ComputeMetrics(9, 7);

        Assert.That(metrics.Stable, Is.False);
        Assert.That(metrics.Rho, Is.EqualTo(9.0 / 7.0).Within(0.001));
        Assert.That(metrics.L, Is.LessThan(0));
        Assert.That(metrics.Lq, Is.LessThan(0));
        Assert.That(metrics.W, Is.LessThan(0));
        Assert.That(metrics.Wq, Is.LessThan(0));
    }

    [Test]
    public void StableNodeMatchesTextbookFormulas()
    {
        var metrics = CMUQueueNetworkMath.ComputeMetrics(9, 12);

        Assert.That(metrics.Stable, Is.True);
        Assert.That(metrics.Rho, Is.EqualTo(0.75).Within(0.001));
        Assert.That(metrics.L, Is.EqualTo(3).Within(0.001));
        Assert.That(metrics.Lq, Is.EqualTo(2.25).Within(0.001));
        Assert.That(metrics.W, Is.EqualTo(1.0 / 3.0).Within(0.001));
        Assert.That(metrics.Wq, Is.EqualTo(0.25).Within(0.001));
    }

    [Test]
    public void RhoOneIsInfiniteAndUnstable()
    {
        var metrics = CMUQueueNetworkMath.ComputeMetrics(10, 10);

        Assert.That(metrics.Stable, Is.False);
        Assert.That(double.IsPositiveInfinity(metrics.L), Is.True);
        Assert.That(double.IsPositiveInfinity(metrics.W), Is.True);
    }

    [Test]
    public void ProjectionGrowsOnlyOverloadedQueues()
    {
        var network = MakeNetwork(
            Node("intake", 12),
            Node("processor", 7),
            Node("packaging", 10));
        network.Sources.Add(Source("intake", 9));
        network.Routes.Add(Route("intake", "processor", 1));
        network.Routes.Add(Route("processor", "packaging", 1));
        network.Routes.Add(Route("packaging", "sink", 1));

        Assert.That(CMUQueueNetworkMath.TrySolveArrivalRates(network, out var rates, out var error), Is.True, error);

        var serviceRates = new Dictionary<string, double>
        {
            ["intake"] = 12,
            ["processor"] = 7,
            ["packaging"] = 10,
        };
        var startQueues = new Dictionary<string, double>
        {
            ["intake"] = 0,
            ["processor"] = 0,
            ["packaging"] = 0,
        };

        var points = CMUQueueNetworkMath.Project(network, serviceRates, rates, startQueues, 3);
        var final = points.Single(point => point.Minute == 3);

        Assert.That(final.Queues["intake"], Is.EqualTo(0).Within(0.001));
        Assert.That(final.Queues["processor"], Is.EqualTo(6).Within(0.001));
        Assert.That(final.Queues["packaging"], Is.EqualTo(0).Within(0.001));
    }

    [Test]
    public void BypassMovesExternalFlowToTarget()
    {
        var network = MakeNetwork(
            Node("intake", 12),
            Node("processor", 10),
            Node("alternate", 10));
        network.Sources.Add(Source("intake", 10));
        network.Routes.Add(Route("intake", "processor", 1));
        network.Routes.Add(Route("processor", "sink", 1));
        network.Routes.Add(Route("alternate", "sink", 1));
        network.Bypasses.Add(new CMUQueueBypassDefinition
        {
            Id = "intake-alternate",
            From = "intake",
            To = "alternate",
            MaxFraction = 0.5,
            StepFraction = 0.1,
            CostPerStep = 100,
        });

        var fractions = new Dictionary<string, double>
        {
            ["intake-alternate"] = 0.5,
        };

        Assert.That(
            CMUQueueNetworkMath.TrySolveArrivalRates(network, fractions, out var rates, out var error),
            Is.True,
            error);

        Assert.That(rates["intake"], Is.EqualTo(5).Within(0.001));
        Assert.That(rates["processor"], Is.EqualTo(5).Within(0.001));
        Assert.That(rates["alternate"], Is.EqualTo(5).Within(0.001));
    }

    [Test]
    public void GeneratorProducesSolvableContracts()
    {
        var topologies = new HashSet<CMUQueueTopologyKind>();
        var bypasses = 0;

        for (var seed = 0; seed < 200; seed++)
        {
            var network = CMUQueueNetworkGenerator.Generate(seed: seed);
            topologies.Add(network.Topology);
            if (network.Bypasses.Count > 0)
                bypasses++;

            Assert.That(network.Nodes.Count, Is.GreaterThanOrEqualTo(3), $"seed {seed}");
            Assert.That(network.Sources, Is.Not.Empty, $"seed {seed}");

            Assert.That(
                CMUQueueNetworkMath.TrySolveArrivalRates(network, out var rates, out var error),
                Is.True,
                $"seed {seed}: {error}");

            var unstable = network.Nodes
                .Where(node => !CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], node.ServiceRate).Stable)
                .ToList();

            Assert.That(unstable, Is.Not.Empty, $"seed {seed} produced no bottleneck");

            var minimumCost = 0;
            foreach (var node in unstable)
            {
                var maxRate = node.ServiceRate * Math.Pow(node.UpgradeMultiplier, node.MaxUpgrades);
                var upgraded = CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], maxRate);
                Assert.That(upgraded.Stable, Is.True, $"seed {seed}: {node.Id} needs more than one upgrade");

                var target = Math.Min(maxRate, rates[node.Id] * 1.02);
                var costPerRate = node.UpgradeCost / (maxRate - node.ServiceRate);
                minimumCost += Math.Max(
                    1,
                    (int) Math.Ceiling((target - node.ServiceRate) * costPerRate - 0.0001));
            }

            Assert.That(
                network.Budget,
                Is.GreaterThanOrEqualTo(minimumCost),
                $"seed {seed}: budget does not cover the minimum stable upgrade");

            Assert.That(network.Reward, Is.GreaterThan(0), $"seed {seed}");

            foreach (var bypass in network.Bypasses)
            {
                Assert.That(network.Sources.Any(source => source.Node == bypass.From), Is.True,
                    $"seed {seed}: bypass starts at a node without external intake");
                Assert.That(network.Nodes.Any(node => node.Id == bypass.To), Is.True,
                    $"seed {seed}: bypass target does not exist");
            }
        }

        Assert.That(topologies, Is.EquivalentTo(Enum.GetValues<CMUQueueTopologyKind>()));
        Assert.That(bypasses, Is.GreaterThan(0));
    }

    [Test]
    public void TutorialGeneratorHasExactlyOneBottleneck()
    {
        var network = CMUQueueNetworkGenerator.Generate(seed: 123, tutorial: true);

        Assert.That(
            CMUQueueNetworkMath.TrySolveArrivalRates(network, out var rates, out var error),
            Is.True,
            error);

        var unstable = network.Nodes
            .Where(node => !CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], node.ServiceRate).Stable)
            .ToList();

        Assert.That(unstable, Has.Count.EqualTo(1));
        Assert.That(network.Bypasses, Is.Empty);
    }

    private static CMUQueueNetworkDefinition MakeNetwork(params CMUQueueNodeDefinition[] nodes)
    {
        return new CMUQueueNetworkDefinition
        {
            Nodes = nodes.ToList(),
        };
    }

    private static CMUQueueNodeDefinition Node(string id, double serviceRate)
    {
        return new CMUQueueNodeDefinition
        {
            Id = id,
            ServiceRate = serviceRate,
        };
    }

    private static CMUQueueSourceDefinition Source(string node, double rate)
    {
        return new CMUQueueSourceDefinition
        {
            Node = node,
            Rate = rate,
        };
    }

    private static CMUQueueRouteDefinition Route(string from, string to, double probability)
    {
        return new CMUQueueRouteDefinition
        {
            From = from,
            To = to,
            Probability = probability,
        };
    }
}
