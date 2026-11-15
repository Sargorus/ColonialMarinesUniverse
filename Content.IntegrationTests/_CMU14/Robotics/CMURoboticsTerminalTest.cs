using System;
using System.Linq;
using Content.Server.CMU14.Chemistry.Research;
using Content.Server.CMU14.Robotics;
using Content.Server.CMU14.Robotics.QueueNetwork;
using Content.Server.Lathe;
using Content.Shared.CMU14.Robotics;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Content.Shared.UserInterface;
using Content.Shared._RMC14.Requisitions.Components;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMURoboticsTerminalTest : GameTest
{
    private CMURoboticsOrderPool Pool(EntityUid console)
        => Server.System<CMURoboticsOrderPoolSystem>().GetPool(console);

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
                Assert.That(state.Cartridges, Has.Count.GreaterThanOrEqualTo(3));
                Assert.That(
                    state.Cartridges.Select(cartridge => cartridge.Prototype.Id),
                    Does.Contain("CMURoboticsCartridgeEmp"));
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
            Assert.That(state.Cartridges, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(
                state.Cartridges.Select(cartridge => cartridge.Description),
                Does.Contain("cmu-robotics-cartridge-emp-desc"));
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
                Assert.That(Pool(console).NextNetworkOrderAllowed, Is.GreaterThan(SGameTiming.CurTime));
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
            Assert.That(state.CooldownSeconds, Is.EqualTo(0), "the network cooldown must not block drone orders");
        });
    }

    [Test]
    public async Task CartridgePurchaseSpendsRoboticsPointsAndUnlocksRecipe()
    {
        var map = await Pair.CreateTestMap();
        NetEntity consoleNet = default;
        EntityUid console = default;
        EntityUid fabricator = default;
        EntityUid user = default;

        await Server.WaitAssertion(() =>
        {
            user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            fabricator = SEntMan.SpawnEntity("CMURoboticsFabricator", map.GridCoords.Offset(new(-2, 0)));
            consoleNet = SEntMan.GetNetEntity(console);

            SEntMan.System<ServerResearchDataTerminalSystem>().UpdateRoboticsPoints(1000, "corporate");
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
        EntityUid cartridge = default;
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ServerResearchDataTerminalSystem>();
            Assert.That(research.GetRoboticsPoints("corporate"), Is.LessThan(1000));

            var cartridges = 0;
            var query = SEntMan.EntityQueryEnumerator<CMURoboticsRecipeCartridgeComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                cartridges++;
                cartridge = uid;
            }

            Assert.That(cartridges, Is.EqualTo(1));

            var fabricatorComp = SComp<CMURoboticsFabricatorComponent>(fabricator);
            Assert.That(
                SEntMan.System<CMURoboticsFabricatorSystem>().TryUnlockCartridge(
                    (fabricator, fabricatorComp),
                    cartridge,
                    user),
                Is.True);
        });

        await Pair.RunTicksSync(1);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(cartridge), Is.False, "the cartridge is consumed");

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
            Assert.That(Pool(console).AvailableOrders, Is.Not.Empty);
            firstOrder = Pool(console).AvailableOrders[0].Id;
        });

        await Send(consoleNet, new CMURoboticsAcceptOrderMessage(firstOrder));
        await Server.WaitAssertion(() =>
        {
            var pool = Pool(console);
            Assert.That(pool.ActiveOrder, Is.Not.Null);
            Assert.That(pool.NextDroneOrderAllowed, Is.GreaterThan(SGameTiming.CurTime));
            Assert.That(pool.NextNetworkOrderAllowed, Is.EqualTo(TimeSpan.Zero));
        });

        await Send(consoleNet, new CMURoboticsCancelOrderMessage());
        await Server.WaitAssertion(() =>
        {
            var terminal = SComp<CMURoboticsOrderConsoleComponent>(console);
            var pool = Pool(console);
            pool.AvailableOrders.Clear();
            var target = terminal.BuildTargets[0];
            pool.AvailableOrders.Add(new CMURoboticsOrder
            {
                Id = 999,
                Type = CMURoboticsOrderType.Build,
                Target = target.Prototype,
                TargetName = "Cooldown target",
                TargetNameLocId = "cmu-robotics-target-civilian-drone",
                Reward = 1000,
                Amount = 1,
            });
            secondOrder = pool.AvailableOrders[0].Id;
        });

        await Send(consoleNet, new CMURoboticsAcceptOrderMessage(secondOrder));
        await Server.WaitAssertion(() =>
        {
            var pool = Pool(console);
            Assert.That(pool.ActiveOrder, Is.Null, "the cooldown blocks the second order");
            Assert.That(pool.Status, Is.Not.Empty);
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
