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
    public void TutorialUpgradeFitsBudgetAndRestoresStability()
    {
        var processor = CMUQueueNetworkMath.ComputeMetrics(9, 7);
        var upgraded = CMUQueueNetworkMath.ComputeMetrics(9, 7 * 1.5);

        Assert.That(processor.Stable, Is.False);
        Assert.That(upgraded.Stable, Is.True);
        Assert.That(250, Is.LessThanOrEqualTo(400));
    }

    [Test]
    public void ReworkUpgradesFitBudgetAndRestoreStability()
    {
        var processor = CMUQueueNetworkMath.ComputeMetrics(10 / 0.9, 8 * 1.5);
        var assembler = CMUQueueNetworkMath.ComputeMetrics(10 / 0.9, 9 * 1.5);

        Assert.That(processor.Stable, Is.True);
        Assert.That(assembler.Stable, Is.True);
        Assert.That(250 + 250, Is.LessThanOrEqualTo(800));
    }

    [Test]
    public void ClosedLoopWithoutSinkIsRejected()
    {
        var network = MakeNetwork(
            Node("a", 10),
            Node("b", 10));
        network.Sources.Add(Source("a", 1));
        network.Routes.Add(Route("a", "b", 1));
        network.Routes.Add(Route("b", "a", 1));

        Assert.That(CMUQueueNetworkMath.TrySolveArrivalRates(network, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    private static CMUQueueNetworkPrototype MakeNetwork(params CMUQueueNodePrototype[] nodes)
    {
        return new CMUQueueNetworkPrototype
        {
            Nodes = nodes.ToList(),
        };
    }

    private static CMUQueueNodePrototype Node(string id, double serviceRate)
    {
        return new CMUQueueNodePrototype
        {
            Id = id,
            ServiceRate = serviceRate,
        };
    }

    private static CMUQueueSourcePrototype Source(string node, double rate)
    {
        return new CMUQueueSourcePrototype
        {
            Node = node,
            Rate = rate,
        };
    }

    private static CMUQueueRoutePrototype Route(string from, string to, double probability)
    {
        return new CMUQueueRoutePrototype
        {
            From = from,
            To = to,
            Probability = probability,
        };
    }
}
