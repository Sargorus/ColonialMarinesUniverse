using System;
using System.Linq;
using Content.Server.CMU14.Chemistry.Research;
using Content.Server.CMU14.Robotics;
using Content.Server.CMU14.Robotics.QueueNetwork;
using Content.Server.Lathe;
using Content.Shared.CMU14.Robotics;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Content.Shared.UserInterface;
using Content.Shared._RMC14.Requisitions.Components;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMURoboticsTerminalTest : GameTest
{
    [Test]
    public async Task TerminalExposesOrdersNetworkAndCartridges()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var queue = SComp<CMUQueueNetworkConsoleComponent>(console);
            queue.ForcedTopology = CMUQueueTopologyKind.Line;
            queue.ForcedSeed = 12345;
            consoleNet = SEntMan.GetNetEntity(console);

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMURoboticsOrderConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CMURoboticsOrderConsoleState>(
                        console,
                        CMURoboticsOrderConsoleUiKey.Key,
                        out var state),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(state!.Queue, Is.Not.Null, "the network tab state is embedded");
                Assert.That(state.Cartridges, Has.Count.EqualTo(1));
                Assert.That(state.Cartridges[0].Prototype.Id, Is.EqualTo("CMURoboticsCartridgeEmp"));
                Assert.That(state.Cartridges[0].Cost, Is.EqualTo(300));
                Assert.That(state.Available, Is.Not.Empty);
            });
        });

        await Client.WaitPost(() =>
        {
            var clientConsole = Client.EntMan.GetEntity(consoleNet);
            Assert.That(
                Client.EntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CMURoboticsOrderConsoleState>(
                        clientConsole,
                        CMURoboticsOrderConsoleUiKey.Key,
                        out var state),
                Is.True);
            Assert.That(state!.Queue, Is.Not.Null, "nested network state survives serialization");
            Assert.That(state.Cartridges, Has.Count.EqualTo(1));
            Assert.That(
                state.Cartridges[0].Description,
                Is.EqualTo("cmu-robotics-cartridge-emp-desc"));
        });

        await Send(consoleNet, new CMUQueueNewContractMessage());
        await Server.WaitAssertion(() =>
        {
            var queue = SComp<CMUQueueNetworkConsoleComponent>(console);
            var terminal = SComp<CMURoboticsOrderConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(queue.Active, Is.True);
                Assert.That(queue.Network, Is.Not.Null);
                Assert.That(terminal.NextOrderAllowed, Is.GreaterThan(SGameTiming.CurTime));
            });
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CMURoboticsOrderConsoleState>(
                        console,
                        CMURoboticsOrderConsoleUiKey.Key,
                        out var state),
                Is.True);
            Assert.That(state!.Queue!.Active, Is.True);
            Assert.That(state.Queue.CooldownSeconds, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task CartridgePurchaseSpendsSampleDataAndUnlocksRecipe()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;
        EntityUid fabricator = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            fabricator = SEntMan.SpawnEntity("CMURoboticsFabricator", map.GridCoords.Offset(new(-2, 0)));
            consoleNet = SEntMan.GetNetEntity(console);

            SEntMan.System<ServerResearchDataTerminalSystem>().UpdateSampleData(1000, "corporate");
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMURoboticsOrderConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        var lathe = SEntMan.GetComponent<LatheComponent>(fabricator);
        var latheSystem = SEntMan.System<LatheSystem>();
        var empRecipe = new ProtoId<LatheRecipePrototype>("CMURoboticsEmpPayload");
        Assert.That(
            latheSystem.GetAvailableRecipes(fabricator, lathe).Contains(empRecipe),
            Is.False);

        await Send(consoleNet, new CMURoboticsPurchaseCartridgeMessage("CMURoboticsCartridgeEmp"));
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ServerResearchDataTerminalSystem>();
            Assert.That(research.GetSampleData("corporate"), Is.EqualTo(700));

            var cartridges = 0;
            EntityUid cartridge = default;
            var query = SEntMan.EntityQueryEnumerator<CMURoboticsRecipeCartridgeComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                cartridges++;
                cartridge = uid;
            }

            Assert.That(cartridges, Is.EqualTo(1));

            var fabricatorComp = SComp<CMURoboticsFabricatorComponent>(fabricator);
            var containers = SEntMan.System<SharedContainerSystem>();
            var container = containers.EnsureContainer<ContainerSlot>(
                fabricator,
                fabricatorComp.CartridgeContainerSlot);
            Assert.That(containers.Insert(cartridge, container), Is.True);

            var recipes = latheSystem.GetAvailableRecipes(fabricator, lathe);
            Assert.That(recipes.Contains(empRecipe), Is.True);
        });
    }

    [Test]
    public async Task OrderCooldownBlocksSecondAccept()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            var elevator = SEntMan.SpawnEntity("CMCargoElevator", map.GridCoords.Offset(new(2, 0)));
            var elevatorComp = SComp<RequisitionsElevatorComponent>(elevator);
            elevatorComp.Faction = "corporate";
            elevatorComp.RoundStartFreeCrateGiven = true;

            console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            consoleNet = SEntMan.GetNetEntity(console);

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CMURoboticsOrderConsoleUiKey.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        var firstOrder = 0;
        var secondOrder = 0;
        await Server.WaitAssertion(() =>
        {
            var terminal = SComp<CMURoboticsOrderConsoleComponent>(console);
            Assert.That(terminal.AvailableOrders, Is.Not.Empty);
            firstOrder = terminal.AvailableOrders[0].Id;
        });

        await Send(consoleNet, new CMURoboticsAcceptOrderMessage(firstOrder));
        await Server.WaitAssertion(() =>
        {
            var terminal = SComp<CMURoboticsOrderConsoleComponent>(console);
            Assert.That(terminal.ActiveOrder, Is.Not.Null);
            Assert.That(terminal.NextOrderAllowed, Is.GreaterThan(SGameTiming.CurTime));
            Assert.That(
                SEntMan.System<CMURoboticsOrderConsoleSystem>().GetCooldownSeconds(terminal),
                Is.GreaterThan(0));
        });

        await Send(consoleNet, new CMURoboticsCancelOrderMessage());
        await Server.WaitAssertion(() =>
        {
            var terminal = SComp<CMURoboticsOrderConsoleComponent>(console);
            terminal.AvailableOrders.Clear();
            var target = SComp<CMURoboticsOrderConsoleComponent>(console).BuildTargets[0];
            terminal.AvailableOrders.Add(new CMURoboticsOrder
            {
                Id = 999,
                Type = CMURoboticsOrderType.Build,
                Target = target.Prototype,
                TargetName = "Cooldown target",
                TargetNameLocId = "cmu-robotics-target-civilian-drone",
                Reward = 1000,
                Amount = 1,
            });
            secondOrder = terminal.AvailableOrders[0].Id;
        });

        await Send(consoleNet, new CMURoboticsAcceptOrderMessage(secondOrder));
        await Server.WaitAssertion(() =>
        {
            var terminal = SComp<CMURoboticsOrderConsoleComponent>(console);
            Assert.That(terminal.ActiveOrder, Is.Null, "the cooldown blocks the second order");
            Assert.That(terminal.Status, Is.Not.Empty);
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CMURoboticsOrderConsoleState>(
                        console,
                        CMURoboticsOrderConsoleUiKey.Key,
                        out var state),
                Is.True);
            Assert.That(state!.CooldownSeconds, Is.GreaterThan(0));
        });
    }

    private async Task Send(NetEntity console, BoundUserInterfaceMessage message)
    {
        await Client.WaitPost(() =>
        {
            Client.EntMan.System<SharedUserInterfaceSystem>().ClientSendUiMessage(
                Client.EntMan.GetEntity(console),
                CMURoboticsOrderConsoleUiKey.Key,
                message);
        });
        await Pair.RunUntilSynced();
    }
}
