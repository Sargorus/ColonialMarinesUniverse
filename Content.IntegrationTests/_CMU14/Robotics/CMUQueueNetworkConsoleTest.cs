using Content.IntegrationTests.Fixtures;
using Content.Client.CMU14.Robotics.QueueNetwork;
using Content.Server.CMU14.Robotics.QueueNetwork;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Content.Shared.UserInterface;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMUQueueNetworkConsoleTest : GameTest
{
    [Test]
    public async Task TutorialContractCompletesAfterUpgrade()
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
            comp.Scenarios.Clear();
            comp.Scenarios.Add(new ProtoId<CMUQueueNetworkPrototype>("CMUQueueNetworkTutorial"));
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
                Assert.That(comp.ActiveScenario?.Id, Is.EqualTo("CMUQueueNetworkTutorial"));
                Assert.That(comp.TimeLeft, Is.EqualTo(15));
                Assert.That(comp.Budget, Is.EqualTo(400));
                Assert.That(comp.Nodes.Count, Is.EqualTo(3));
            });
        });

        await Send(consoleNet, new CMUQueueProjectMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.TimeLeft, Is.EqualTo(12));
                Assert.That(comp.History.Count, Is.GreaterThan(1));
                Assert.That(comp.Nodes["processor"].Queue, Is.EqualTo(6).Within(0.001));
            });
        });

        await Send(consoleNet, new CMUQueueAnalyzeNodeMessage("processor"));
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Analyses.ContainsKey("processor"), Is.True);
                Assert.That(comp.Analyses["processor"].Metrics.Stable, Is.False);
                Assert.That(comp.Analyses["processor"].Metrics.L, Is.LessThan(0));
                Assert.That(comp.TimeLeft, Is.EqualTo(11));
            });
        });

        await Send(consoleNet, new CMUQueueUpgradeNodeMessage("processor"));
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Nodes["processor"].Upgrades, Is.EqualTo(1));
                Assert.That(comp.Nodes["processor"].ServiceRate, Is.EqualTo(10.5).Within(0.001));
                Assert.That(comp.Analyses["processor"].Metrics.Stable, Is.True);
                Assert.That(comp.Budget, Is.EqualTo(150));
            });
        });

        await Send(consoleNet, new CMUQueueProjectMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.TimeLeft, Is.EqualTo(8));
                Assert.That(comp.Nodes["processor"].Queue, Is.LessThan(6));
            });
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
                    Is.EqualTo(budgetBefore + 800).Within(0.001));
            });
        });
    }

    [Test]
    public async Task ReworkScenarioRequiresBothBottlenecks()
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
            comp.Scenarios.Clear();
            comp.Scenarios.Add(new ProtoId<CMUQueueNetworkPrototype>("CMUQueueNetworkRework"));
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
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Active, Is.True);
                Assert.That(comp.Budget, Is.EqualTo(800));
                Assert.That(comp.Nodes.Count, Is.EqualTo(5));
            });
        });

        await Send(consoleNet, new CMUQueueAnalyzeNodeMessage("processor"));
        await Send(consoleNet, new CMUQueueAnalyzeNodeMessage("assembler"));
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Analyses["processor"].Metrics.Stable, Is.False);
                Assert.That(comp.Analyses["assembler"].Metrics.Stable, Is.False);
            });
        });

        await Send(consoleNet, new CMUQueueUpgradeNodeMessage("processor"));
        await Send(consoleNet, new CMUQueueSubmitMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Completed, Is.False);
                Assert.That(comp.Active, Is.True);
                Assert.That(comp.Budget, Is.EqualTo(550));
            });
        });

        await Send(consoleNet, new CMUQueueUpgradeNodeMessage("assembler"));
        await Send(consoleNet, new CMUQueueSubmitMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Completed, Is.True);
                Assert.That(comp.Active, Is.False);
                Assert.That(comp.Budget, Is.EqualTo(300));
            });
        });
    }

    [Test]
    public async Task ResetUpgradesRefundsBudgetAndRestoresBaseRates()
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
            comp.Scenarios.Clear();
            comp.Scenarios.Add(new ProtoId<CMUQueueNetworkPrototype>("CMUQueueNetworkTutorial"));
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
        await Send(consoleNet, new CMUQueueAnalyzeNodeMessage("processor"));
        await Send(consoleNet, new CMUQueueUpgradeNodeMessage("processor"));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<CMUQueueNetworkConsoleComponent>(console).Budget, Is.EqualTo(150));
        });

        await Send(consoleNet, new CMUQueueResetUpgradesMessage());
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CMUQueueNetworkConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Budget, Is.EqualTo(400));
                Assert.That(comp.Nodes["processor"].Upgrades, Is.EqualTo(0));
                Assert.That(comp.Nodes["processor"].ServiceRate, Is.EqualTo(7).Within(0.001));
                Assert.That(comp.Analyses["processor"].Metrics.Stable, Is.False);
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
            comp.Scenarios.Clear();
            comp.Scenarios.Add(new ProtoId<CMUQueueNetworkPrototype>("CMUQueueNetworkTutorial"));
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
        await Server.WaitAssertion(() =>
        {
            SComp<CMUQueueNetworkConsoleComponent>(console).TimeLeft = 0;
        });

        await Send(consoleNet, new CMUQueueAnalyzeNodeMessage("processor"));
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
        await Server.WaitPost(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            Assert.That(loc.TryGetString("cmu-queue-console-title", out var title), Is.True);
            Assert.That(title, Is.Not.EqualTo("cmu-queue-console-title"));
            Assert.That(loc.TryGetString("cmu-queue-formulas-negative-p2", out var body), Is.True);
            Assert.That(body, Is.Not.EqualTo("cmu-queue-formulas-negative-p2"));
        });

        await Client.WaitPost(() =>
        {
            var window = new CMUQueueFormulasWindow();
            window.OpenCentered();
            window.Close();
        });
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
