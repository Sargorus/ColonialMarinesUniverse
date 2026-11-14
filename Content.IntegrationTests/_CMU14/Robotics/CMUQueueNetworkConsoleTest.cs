using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Client.CMU14.Robotics.QueueNetwork;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Robotics.QueueNetwork;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Content.Shared.UserInterface;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMUQueueNetworkConsoleTest : GameTest
{
    [Test]
    public async Task GeneratedLineContractCompletesAfterUpgradingBottlenecks()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;
        EntityUid corporate = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            corporate = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords.Offset(new(-2, 0)));
            console = SEntMan.SpawnEntity("CMUProductionDiagnosticConsole", map.GridCoords);
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            comp.ForcedTopology = CMUQueueTopologyKind.Line;
            comp.ForcedSeed = 12345;
            consoleNet = SEntMan.GetNetEntity(console);

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMUQueueNetworkConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        var budgetBefore = 0f;
        await Server.WaitAssertion(() =>
        {
            budgetBefore = SComp<CorporateConsoleComponent>(corporate).CorporateBudget;
        });

        await Send(consoleNet, new CMUQueueNewContractMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Active, Is.True);
                Assert.That(comp.Network, Is.Not.Null);
                Assert.That(comp.Network!.Topology, Is.EqualTo(CMUQueueTopologyKind.Line));
                Assert.That(comp.Nodes.Count, Is.GreaterThanOrEqualTo(3));
                Assert.That(comp.TimeLeft, Is.EqualTo(15));
            });
        });

        var unstableIds = new List<string>();
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            var network = comp.Network!;
            Assert.That(
                CMUQueueNetworkMath.TrySolveArrivalRates(network, comp.BypassFractions, out var rates, out var error),
                Is.True,
                error);

            unstableIds = network.Nodes
                .Where(node => !CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], node.ServiceRate).Stable)
                .Select(node => node.Id)
                .ToList();
        });

        Assert.That(unstableIds, Is.Not.Empty);

        await Send(consoleNet, new CMUQueueAnalyzeNodeMessage(unstableIds[0]));
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Analyses.ContainsKey(unstableIds[0]), Is.True);
                Assert.That(comp.Analyses[unstableIds[0]].Metrics.Stable, Is.False);
                Assert.That(comp.Analyses[unstableIds[0]].Metrics.L, Is.LessThan(0));
            });
        });

        foreach (var nodeId in unstableIds)
            await SetStableRate(console, consoleNet, nodeId);

        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            var network = comp.Network!;
            Assert.That(
                CMUQueueNetworkMath.TrySolveArrivalRates(network, comp.BypassFractions, out var rates, out var error),
                Is.True,
                error);

            foreach (var node in network.Nodes)
            {
                var metrics = CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], comp.Nodes[node.Id].ServiceRate);
                Assert.That(metrics.Stable, Is.True, $"{node.Id} is still unstable");
            }
        });

        await Send(consoleNet, new CMUQueueProjectMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.That(comp.History.Count, Is.GreaterThan(1));
        });

        await Send(consoleNet, new CMUQueueSubmitMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Completed, Is.True);
                Assert.That(comp.Active, Is.False);
                Assert.That(
                    SComp<CorporateConsoleComponent>(corporate).CorporateBudget,
                    Is.EqualTo(budgetBefore + comp.Reward).Within(0.001));
            });
        });
    }

    [Test]
    public async Task ReworkTopologyCanBeStabilized()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("CMUProductionDiagnosticConsole", map.GridCoords);
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            comp.ForcedTopology = CMUQueueTopologyKind.Rework;
            comp.ForcedSeed = 6789;
            consoleNet = SEntMan.GetNetEntity(console);

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMUQueueNetworkConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Send(consoleNet, new CMUQueueNewContractMessage());
        var unstableIds = new List<string>();
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            var network = comp.Network!;
            Assert.That(network.Topology, Is.EqualTo(CMUQueueTopologyKind.Rework));
            Assert.That(
                CMUQueueNetworkMath.TrySolveArrivalRates(network, comp.BypassFractions, out var rates, out var error),
                Is.True,
                error);

            unstableIds = network.Nodes
                .Where(node => !CMUQueueNetworkMath.ComputeMetrics(rates[node.Id], node.ServiceRate).Stable)
                .Select(node => node.Id)
                .ToList();
        });

        Assert.That(unstableIds, Has.Count.EqualTo(2));

        foreach (var nodeId in unstableIds)
            await SetStableRate(console, consoleNet, nodeId);

        await Send(consoleNet, new CMUQueueSubmitMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.That(comp.Completed, Is.True);
        });
    }

    [Test]
    public async Task BypassChangesArrivalRatesAndRefunds()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("CMUProductionDiagnosticConsole", map.GridCoords);
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            comp.ForcedTopology = CMUQueueTopologyKind.Branch;
            comp.ForcedSeed = 424242;
            consoleNet = SEntMan.GetNetEntity(console);

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMUQueueNetworkConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Send(consoleNet, new CMUQueueNewContractMessage());

        var bypassId = string.Empty;
        var fromNode = string.Empty;
        var toNode = string.Empty;
        var lambdaBefore = 0.0;
        var targetBefore = 0.0;
        var budgetBefore = 0;

        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            var network = comp.Network!;
            Assert.That(network.Bypasses, Is.Not.Empty);

            var bypass = network.Bypasses[0];
            bypassId = bypass.Id;
            fromNode = bypass.From;
            toNode = bypass.To;
            budgetBefore = comp.Budget;

            Assert.That(
                CMUQueueNetworkMath.TrySolveArrivalRates(network, comp.BypassFractions, out var rates, out var error),
                Is.True,
                error);
            lambdaBefore = rates[fromNode];
            targetBefore = rates[toNode];
        });

        await Send(consoleNet, new CMUQueueRerouteMessage(bypassId));
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            var network = comp.Network!;

            Assert.That(comp.BypassFractions[bypassId], Is.GreaterThan(0));
            Assert.That(comp.Budget, Is.LessThan(budgetBefore));

            Assert.That(
                CMUQueueNetworkMath.TrySolveArrivalRates(network, comp.BypassFractions, out var rates, out var error),
                Is.True,
                error);

            Assert.That(rates[fromNode], Is.LessThan(lambdaBefore));
            Assert.That(rates[toNode], Is.GreaterThan(targetBefore));
        });

        await Send(consoleNet, new CMUQueueResetInterventionsMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.BypassFractions[bypassId], Is.EqualTo(0).Within(0.001));
                Assert.That(comp.Budget, Is.EqualTo(budgetBefore));
            });
        });
    }

    [Test]
    public async Task AnalyzerRefusesToSpendMissingTime()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("CMUProductionDiagnosticConsole", map.GridCoords);
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            comp.ForcedTopology = CMUQueueTopologyKind.Line;
            comp.ForcedSeed = 12345;
            consoleNet = SEntMan.GetNetEntity(console);

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMUQueueNetworkConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Send(consoleNet, new CMUQueueNewContractMessage());
        string nodeId = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            comp.TimeLeft = 0;
            nodeId = comp.Network!.Nodes[0].Id;
        });

        await Send(consoleNet, new CMUQueueAnalyzeNodeMessage(nodeId));
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.That(comp.Analyses, Is.Empty);
            Assert.That(comp.TimeLeft, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ReferenceKeysAreLocalized()
    {
        await Server.WaitAssertion(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            Assert.That(loc.TryGetString("cmu-queue-console-title", out var title), Is.True);
            Assert.That(title, Is.Not.EqualTo("cmu-queue-console-title"));
            Assert.That(loc.TryGetString("cmu-queue-formulas-negative-p2", out var body), Is.True);
            Assert.That(body, Is.Not.EqualTo("cmu-queue-formulas-negative-p2"));
            Assert.That(loc.TryGetString("ent-CMUProductionDiagnosticConsole", out var terminal), Is.True);
            Assert.That(terminal, Is.Not.EqualTo("ent-CMUProductionDiagnosticConsole"));
            Assert.That(loc.TryGetString("ent-CMUProductionDiagnosticConsole-desc", out var terminalDesc), Is.True);
            Assert.That(terminalDesc, Is.Not.EqualTo("ent-CMUProductionDiagnosticConsole-desc"));
        });

        await Client.WaitPost(() =>
        {
            var loc = Client.ResolveDependency<ILocalizationManager>();
            var original = CultureInfo.CurrentCulture;

            try
            {
                loc.SetCulture(new CultureInfo("ru-RU"));
                var window = new CMUQueueFormulasWindow();
                window.OpenCentered();
                window.Close();
            }
            finally
            {
                loc.SetCulture(original);
            }
        });
    }

    [Test]
    public async Task ResetRefundsUpgradesAndDiversions()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("CMUProductionDiagnosticConsole", map.GridCoords);
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            comp.ForcedTopology = CMUQueueTopologyKind.Branch;
            comp.ForcedSeed = 424242;
            consoleNet = SEntMan.GetNetEntity(console);

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMUQueueNetworkConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Send(consoleNet, new CMUQueueNewContractMessage());

        var nodeId = string.Empty;
        var baseRate = 0.0;
        var bypassId = string.Empty;

        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            comp.Budget = 2000;
            comp.InitialBudget = 2000;

            var node = comp.Network!.Nodes[0];
            nodeId = node.Id;
            baseRate = node.ServiceRate;
            bypassId = comp.Network.Bypasses[0].Id;
        });

        await Send(consoleNet, new CMUQueueSetServiceRateMessage(nodeId, baseRate + 0.5));
        await Send(consoleNet, new CMUQueueRerouteMessage(bypassId));
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.That(comp.Budget, Is.LessThan(2000));
            Assert.That(comp.Nodes[nodeId].Invested, Is.GreaterThan(0));
            Assert.That(comp.Nodes[nodeId].ServiceRate, Is.GreaterThan(baseRate));
            Assert.That(comp.BypassFractions[bypassId], Is.GreaterThan(0));
        });

        await Send(consoleNet, new CMUQueueResetInterventionsMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Budget, Is.EqualTo(2000));
                Assert.That(comp.Nodes[nodeId].Invested, Is.EqualTo(0));
                Assert.That(comp.Nodes[nodeId].ServiceRate, Is.EqualTo(baseRate).Within(0.001));
                Assert.That(comp.BypassFractions[bypassId], Is.EqualTo(0).Within(0.001));
            });
        });
    }

    [Test]
    public async Task WindowHandlesLongLocalizedText()
    {
        await Client.WaitPost(() =>
        {
            var loc = Client.ResolveDependency<ILocalizationManager>();
            var original = CultureInfo.CurrentCulture;

            try
            {
                loc.SetCulture(new CultureInfo("ru-RU"));
                var history = new List<CMUQueueHistoryInfo>();
                for (var step = 0; step <= 12; step++)
                {
                    var minute = step * 0.25f;
                    var queue = Math.Min(6f, step * 0.5f);
                    history.Add(new CMUQueueHistoryInfo(minute, new List<float> { queue, 0f }));
                }

                var state = new CMUQueueNetworkConsoleState(
                    true,
                    false,
                    "status",
                    CMUQueueStatusKind.Neutral,
                    "cmu-queue-network-branch-name",
                    "cmu-queue-network-branch-desc",
                    800,
                    15,
                    15,
                    1200,
                    0,
                    1,
                    3,
                    3,
                    new List<CMUQueueNodeInfo>
                    {
                        new(
                            "long",
                            "cmu-queue-node-intake",
                            "LONG-SUFFIX",
                            12,
                            10,
                            20,
                            10,
                            6,
                            250,
                            12.5,
                            250,
                            0,
                            1,
                            true,
                            9,
                            0.75,
                            3,
                            2.25,
                            0.33,
                            0.25,
                            true),
                        new(
                            "second",
                            "cmu-queue-node-processor",
                            "A",
                            8,
                            8,
                            12,
                            0,
                            0,
                            0,
                            12.5,
                            250,
                            1,
                            0,
                            false,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            false),
                    },
                    new List<CMUQueueRouteInfo>
                    {
                        new("long", "second", 1),
                        new("second", "sink", 1),
                    },
                    new List<CMUQueueBypassInfo>
                    {
                        new("long-second", "long", "second", 0.1, 0.3, 0.1, 100, true, false),
                    },
                    history);

                var window = new CMUQueueNetworkConsoleWindow();
                window.UpdateState(state);
                window.OpenCentered();
                window.Close();
            }
            finally
            {
                loc.SetCulture(original);
            }
        });
    }

    [Test]
    public async Task RussianKeysAreLocalized()
    {
        await Server.WaitAssertion(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            var original = CultureInfo.CurrentCulture;

            try
            {
                loc.SetCulture(new CultureInfo("ru-RU"));
                Assert.That(loc.TryGetString("cmu-queue-console-title", out var title), Is.True);
                Assert.That(title, Is.Not.EqualTo("cmu-queue-console-title"));
                Assert.That(loc.TryGetString("cmu-queue-formulas-negative-p2", out var body), Is.True);
                Assert.That(body, Is.Not.EqualTo("cmu-queue-formulas-negative-p2"));
            }
            finally
            {
                loc.SetCulture(original);
            }
        });
    }

    private async Task SetStableRate(EntityUid console, NetEntity consoleNet, string nodeId)
    {
        var target = 0.0;
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            var network = comp.Network!;
            Assert.That(
                CMUQueueNetworkMath.TrySolveArrivalRates(
                    network,
                    comp.BypassFractions,
                    out var rates,
                    out var error),
                Is.True,
                error);

            var node = network.Nodes.First(candidate => candidate.Id == nodeId);
            var maxRate = node.ServiceRate * Math.Pow(node.UpgradeMultiplier, node.MaxUpgrades);
            target = Math.Min(maxRate, rates[nodeId] * 1.02);
        });

        await Send(consoleNet, new CMUQueueSetServiceRateMessage(nodeId, target));
    }

    private async Task Send(NetEntity console, BoundUserInterfaceMessage message)
    {
        await Client.WaitPost(() =>
        {
            Client.EntMan.System<SharedUserInterfaceSystem>().ClientSendUiMessage(
                Client.EntMan.GetEntity(console),
                CMUQueueNetworkConsoleUiKey.Key,
                message);
        });
        await Pair.RunUntilSynced();
    }
}
