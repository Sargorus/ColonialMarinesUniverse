using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client.CMU14.Robotics.UtilityDrones;
using Content.IntegrationTests.Fixtures;
using Content.Server.Chat.Systems;
using Content.Server.CMU14.Robotics.UtilityDrones;
using Content.Server.CMU14.ZLevels.Core;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.CMU14.ZLevels.Core.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Chat;
using Content.Shared.Gravity;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._RMC14.Synth;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.CMU14.UtilityDrones;

[TestFixture]
public sealed class CMUUtilityDronesTest : GameTest
{
    private CMUUtilityDroneSystem Drones => SEntMan.System<CMUUtilityDroneSystem>();
    private CMUUtilityDroneConsoleSystem Consoles => SEntMan.System<CMUUtilityDroneConsoleSystem>();
    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();
    private SharedTransformSystem Transforms => SEntMan.System<SharedTransformSystem>();

    private async Task<TestMapData> CreateFloorMap()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
        });
        await Pair.RunTicksSync(60);
        return map;
    }

    private (EntityUid Console, EntityUid Drone) SpawnPair(
        TestMapData map,
        Vector2 console,
        Vector2 drone,
        string dronePrototype = "CMUUtilityDrone")
    {
        var consoleUid = SEntMan.SpawnEntity("CMUUtilityDroneConsole", map.GridCoords.Offset(console));
        var droneUid = SEntMan.SpawnEntity(dronePrototype, map.GridCoords.Offset(drone));
        return (consoleUid, droneUid);
    }

    private EntityUid CreateMarkerAt(EntityUid console, EntityCoordinates coordinates)
    {
        var marker = SEntMan.SpawnEntity("CMUUtilityTaskMarker", coordinates);
        Assert.That(Transforms.AnchorEntity(marker), Is.True);
        Assert.That(Consoles.TryLinkMarker(console, marker, console, out var error), Is.True, error);
        return marker;
    }

    private EntityUid CreateMarker(
        EntityUid console,
        TestMapData map,
        Vector2 position)
    {
        return CreateMarkerAt(console, map.GridCoords.Offset(position));
    }

    private EntityUid PrintBoard(EntityUid console, params CMUUtilityStepInfo[] steps)
    {
        return PrintBoardWithRules(console, new List<CMUUtilityRuleInfo>(), steps);
    }

    private EntityUid PrintScriptBoard(
        EntityUid console,
        string script,
        List<CMUUtilityRuleInfo>? rules = null)
    {
        var program = new CMUUtilityProgramInfo(
            new List<CMUUtilityStepInfo>(),
            rules ?? new List<CMUUtilityRuleInfo>(),
            false,
            30f)
        {
            Script = script,
        };

        Assert.That(
            Consoles.TryPrintProgramBoard(console, default, program, out var board, out var error),
            Is.True,
            error);
        return board;
    }

    private EntityUid InstallScript(
        EntityUid console,
        EntityUid drone,
        TestMapData map,
        Vector2 installerPosition,
        string script,
        List<CMUUtilityRuleInfo>? rules = null)
    {
        var board = PrintScriptBoard(console, script, rules);
        var installer = CreateInstaller(map, installerPosition, board);
        Assert.That(Drones.TrySetProgrammableMode(drone, true, out var modeError), Is.True, modeError);
        Assert.That(Drones.TrySetBoardPortLocked(drone, false, out var portError), Is.True, portError);
        Assert.That(Drones.TryInstallProgramBoard(drone, board, installer, out var error), Is.True, error);
        return installer;
    }

    private EntityUid PrintBoardWithRules(
        EntityUid console,
        List<CMUUtilityRuleInfo> rules,
        params CMUUtilityStepInfo[] steps)
    {
        var program = new CMUUtilityProgramInfo(
            steps.ToList(),
            rules,
            false,
            30f);

        Assert.That(
            Consoles.TryPrintProgramBoard(console, default, program, out var board, out var error),
            Is.True,
            error);
        return board;
    }

    private EntityUid CreateInstaller(TestMapData map, Vector2 position, EntityUid board)
    {
        var installer = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(position));
        Assert.That(Hands.TryPickup(installer, board), Is.True);
        var multitool = SEntMan.SpawnEntity("CMMultitool", map.GridCoords.Offset(position));
        Assert.That(Hands.TryPickupAnyHand(installer, multitool), Is.True);
        return installer;
    }

    private EntityUid InstallProgram(
        EntityUid console,
        EntityUid drone,
        TestMapData map,
        Vector2 installerPosition,
        params CMUUtilityStepInfo[] steps)
    {
        return InstallProgramWithRules(
            console,
            drone,
            map,
            installerPosition,
            new List<CMUUtilityRuleInfo>(),
            steps);
    }

    private EntityUid InstallProgramWithRules(
        EntityUid console,
        EntityUid drone,
        TestMapData map,
        Vector2 installerPosition,
        List<CMUUtilityRuleInfo> rules,
        params CMUUtilityStepInfo[] steps)
    {
        var board = PrintBoardWithRules(console, rules, steps);
        var installer = CreateInstaller(map, installerPosition, board);
        Assert.That(Drones.TrySetProgrammableMode(drone, true, out var modeError), Is.True, modeError);
        Assert.That(Drones.TrySetBoardPortLocked(drone, false, out var portError), Is.True, portError);
        Assert.That(Drones.TryInstallProgramBoard(drone, board, installer, out var error), Is.True, error);
        return installer;
    }

    private CMUUtilityDroneAgentComponent Agent(EntityUid drone)
    {
        return SEntMan.GetComponent<CMUUtilityDroneAgentComponent>(drone);
    }

    private NetEntity Net(EntityUid uid)
    {
        return SEntMan.GetNetEntity(uid);
    }

    [Test]
    public async Task ThreeStepProgramRunsToCompletion()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var first = CreateMarker(console, map, new Vector2(4, 0));
            target = CreateMarker(console, map, new Vector2(-6, 0));
            InstallProgram(console, drone, map, new Vector2(-3, 1),
                new CMUUtilityStepInfo(CMUUtilityStepKind.MoveTo, Net(first), 4f, 0f, string.Empty),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Wait, NetEntity.Invalid, 1f, 0.5f, string.Empty),
                new CMUUtilityStepInfo(CMUUtilityStepKind.MoveTo, Net(target), 4f, 0f, string.Empty));
        });

        await Pair.RunTicksSync(600);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            var debugPosition = SEntMan.GetComponent<TransformComponent>(drone).Coordinates;
            Assert.That(agent.ProgramRunning, Is.False,
                $"step={agent.StepIndex}, status={agent.Status}, activity={agent.Activity}, path={agent.PathAttempts}, position={debugPosition}");
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            Assert.That(agent.StepsCompleted, Is.GreaterThanOrEqualTo(3));
            Assert.That(agent.ProgramBoard, Is.Not.Null);
            Assert.That(agent.ProgramBoardContainer?.ContainedEntity, Is.EqualTo(agent.ProgramBoard));
            var position = SEntMan.GetComponent<TransformComponent>(drone).Coordinates;
            Assert.That(position.TryDistance(SEntMan, SEntMan.GetComponent<TransformComponent>(target).Coordinates, out var distance), Is.True);
            Assert.That(distance, Is.LessThan(2.5f));
        });
    }

    [Test]
    public async Task MaliciousProgramsAreRejected()
    {
        var map = await CreateFloorMap();
        EntityUid console = default;
        EntityUid marker = default;
        EntityUid unanchored = default;
        EntityUid foreignMarker = default;
        await Server.WaitAssertion(() =>
        {
            var pair = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            console = pair.Console;
            marker = CreateMarker(console, map, new Vector2(2, 0));

            unanchored = SEntMan.SpawnEntity("CMUUtilityTaskMarker", map.GridCoords.Offset(new Vector2(3, 0)));
            Assert.That(Consoles.TryLinkMarker(console, unanchored, console, out _), Is.False);

            var foreignPair = SpawnPair(map, new Vector2(10, 10), new Vector2(11, 10));
            foreignMarker = CreateMarker(foreignPair.Console, map, new Vector2(12, 10));

            var invalidEnum = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>
                {
                    new((CMUUtilityStepKind) 250, Net(marker), 4f, 1f, string.Empty),
                },
                new List<CMUUtilityRuleInfo>(),
                false,
                30f);
            Assert.That(Drones.ValidateProgram(console, invalidEnum, out _, out var enumError), Is.False);
            Assert.That(enumError, Is.Not.Empty);

            var longNote = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>
                {
                    new(CMUUtilityStepKind.MoveTo, Net(marker), 4f, 1f, new string('x', 129)),
                },
                new List<CMUUtilityRuleInfo>(),
                false,
                30f);
            Assert.That(Drones.ValidateProgram(console, longNote, out _, out var stringError), Is.False);
            Assert.That(stringError, Is.Not.Empty);

            var tooMany = new CMUUtilityProgramInfo(
                Enumerable.Repeat(
                    new CMUUtilityStepInfo(CMUUtilityStepKind.Wait, NetEntity.Invalid, 1f, 0f, string.Empty),
                    CMUUtilityDroneLimits.MaxSteps + 1).ToList(),
                new List<CMUUtilityRuleInfo>(),
                false,
                30f);
            Assert.That(Drones.ValidateProgram(console, tooMany, out _, out var countError), Is.False);
            Assert.That(countError, Is.Not.Empty);

            var foreignProgram = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>
                {
                    new(CMUUtilityStepKind.MoveTo, Net(foreignMarker), 4f, 1f, string.Empty),
                },
                new List<CMUUtilityRuleInfo>(),
                false,
                30f);
            Assert.That(Drones.ValidateProgram(console, foreignProgram, out _, out var markerError), Is.False);
            Assert.That(markerError, Is.Not.Empty);

            var unanchoredProgram = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>
                {
                    new(CMUUtilityStepKind.MoveTo, Net(unanchored), 4f, 1f, string.Empty),
                },
                new List<CMUUtilityRuleInfo>(),
                false,
                30f);
            Assert.That(Drones.ValidateProgram(console, unanchoredProgram, out _, out var anchorError), Is.False);
            Assert.That(anchorError, Is.Not.Empty);
            Assert.That(SEntMan.GetComponent<CMUUtilityDroneConsoleComponent>(console).ValidationError, Is.Empty);
        });
    }

    [Test]
    public async Task FifteenDronesPatrolWithoutFailures()
    {
        var map = await CreateFloorMap();
        var drones = new List<EntityUid>();
        EntityUid console = default;
        EntityUid marker = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("CMUUtilityDroneConsole", map.GridCoords.Offset(new Vector2(-4, 0)));
            marker = CreateMarker(console, map, new Vector2(3, 0));
            for (var i = 0; i < CMUUtilityDroneLimits.MaxDrones; i++)
            {
                var angle = i * MathF.Tau / CMUUtilityDroneLimits.MaxDrones;
                var position = new Vector2(-2, 0) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 1.5f;
                var drone = SEntMan.SpawnEntity("CMUUtilityDrone", map.GridCoords.Offset(position));
                drones.Add(drone);
                InstallProgram(console, drone, map, new Vector2(-4, 1),
                    new CMUUtilityStepInfo(CMUUtilityStepKind.Patrol, Net(marker), 2.5f, 1f, string.Empty));
            }
        });

        await Pair.RunTicksSync(900);
        await Server.WaitAssertion(() =>
        {
            var markerPosition = SEntMan.GetComponent<TransformComponent>(marker).Coordinates;
            foreach (var drone in drones)
            {
                var agent = Agent(drone);
                Assert.That(agent.ProgramFailures, Is.EqualTo(0), $"Drone {agent.Number} failed its patrol.");
                Assert.That(agent.StepPhase > 0 || agent.StepsCompleted > 0, Is.True,
                    $"Drone {agent.Number} made no waypoint progress; status={agent.Status}, activity={agent.Activity}, step={agent.StepIndex}, path={agent.PathAttempts}.");
                var position = SEntMan.GetComponent<TransformComponent>(drone).Coordinates;
                Assert.That(position.TryDistance(SEntMan, markerPosition, out var distance), Is.True);
                Assert.That(distance, Is.LessThan(10f), $"Drone {agent.Number} left the patrol area.");
            }
        });
    }

    [Test]
    public async Task UnreachableMarkerFailsAfterBoundedPathAttempts()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var target = new Vector2(10, 10);
            var marker = CreateMarker(console, map, target);
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                SEntMan.SpawnEntity("CMWallMetal", map.GridCoords.Offset(target + new Vector2(x, y)));
            }

            InstallProgram(console, drone, map, new Vector2(-3, 1),
                new CMUUtilityStepInfo(CMUUtilityStepKind.MoveTo, Net(marker), 4f, 0f, string.Empty));
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramRunning, Is.False);
            Assert.That(agent.ProgramFailures, Is.GreaterThan(0));
            Assert.That(agent.Status, Is.EqualTo(CMUUtilityDroneStatus.Failed));
        });
    }

    [Test]
    public async Task DroneClimbsStairsToMarkerOnAnotherZLevel()
    {
        var maps = new List<EntityUid>();
        EntityUid network = default;
        EntityUid drone = default;
        EntityUid marker = default;
        try
        {
            await Server.WaitAssertion(() =>
            {
                var mapSystem = SEntMan.System<SharedMapSystem>();
                var floor = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                for (var i = 0; i < 2; i++)
                {
                    var map = mapSystem.CreateMap(runMapInit: true);
                    maps.Add(map);
                    var grid = SEntMan.EnsureComponent<MapGridComponent>(map);
                    var gravity = SEntMan.EnsureComponent<GravityComponent>(map);
                    gravity.Enabled = true;
                    gravity.Inherent = true;
                    for (var x = -12; x <= 12; x++)
                    for (var y = -12; y <= 12; y++)
                        mapSystem.SetTile(map, grid, new Vector2i(x, y), floor);
                }

                var z = SEntMan.System<CMUZLevelsSystem>();
                var net = z.CreateZNetwork();
                network = net;
                Assert.That(z.TryAddMapsIntoZNetwork(net, new() { [maps[0]] = 0, [maps[1]] = 1 }), Is.True);
            });

            await Pair.RunTicksSync(60);
            await Server.WaitAssertion(() =>
            {
                var console = SEntMan.SpawnEntity("CMUUtilityDroneConsole", new EntityCoordinates(maps[0], new Vector2(-4.5f, 0.5f)));
                drone = SEntMan.SpawnEntity("CMUUtilityDrone", new EntityCoordinates(maps[0], new Vector2(-3.5f, 0.5f)));

                var stair = SEntMan.SpawnEntity("CMUSwarmGuardMarker", new EntityCoordinates(maps[0], new Vector2(0.5f, 0.5f)));
                var stairData = SEntMan.AddComponent<CMUZLevelStairsComponent>(stair);
                stairData.Direction = Direction.North;
                stairData.Offset = 1;

                marker = CreateMarkerAt(console, new EntityCoordinates(maps[1], new Vector2(4.5f, 0.5f)));
                InstallProgramOnMaps(console, drone, maps[0], new Vector2(-4.5f, 1.5f),
                    new CMUUtilityStepInfo(CMUUtilityStepKind.MoveTo, Net(marker), 4f, 0f, string.Empty));
            });

            await Pair.RunTicksSync(2400);
            await Server.WaitAssertion(() =>
            {
                var xform = SEntMan.GetComponent<TransformComponent>(drone);
                var agent = Agent(drone);
                Assert.That(xform.MapUid, Is.EqualTo(maps[1]), "The drone must physically use the stairs.");
                Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
                Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
                Assert.That(
                    Vector2.Distance(
                        Transforms.GetWorldPosition(drone),
                        Transforms.GetWorldPosition(marker)),
                    Is.LessThan(4f));
            });
        }
        finally
        {
            foreach (var map in maps)
                await Pair.DeleteEntityTreeLeafFirst(map);
            if (network.IsValid())
                await Pair.DeleteEntityTreeLeafFirst(network);
        }
    }

    [Test]
    public async Task FetchesItemAndDeliversItIntoNearbyStorage()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid item = default;
        EntityUid crate = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            item = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(2, 0)));
            crate = SEntMan.SpawnEntity("CrateGenericSteel", map.GridCoords.Offset(new Vector2(6, 0)));
            var fetch = CreateMarker(console, map, new Vector2(2, 0));
            var deliver = CreateMarker(console, map, new Vector2(6, 0));
            InstallProgram(console, drone, map, new Vector2(-3, 1),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Fetch, Net(fetch), 3f, 0f, string.Empty),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Deliver, Net(deliver), 3f, 0f, string.Empty));
        });

        await Pair.RunTicksSync(900);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Agent(drone).ProgramFailures, Is.EqualTo(0));
            Assert.That(SEntMan.TryGetComponent<EntityStorageComponent>(crate, out var storage), Is.True);
            Assert.That(storage!.Contents.ContainedEntities, Does.Contain(item));
        });
    }

    [Test]
    public async Task RepairsDamagedBarricadeWithWelder()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid barricade = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            barricade = SEntMan.SpawnEntity("CMBarricadeMetal", map.GridCoords.Offset(new Vector2(4, 0)));
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(80) } };
            SEntMan.System<DamageableSystem>().SetDamage(
                (barricade, SEntMan.GetComponent<DamageableComponent>(barricade)),
                damage);

            var welder = SEntMan.SpawnEntity("CMWelder", map.GridCoords.Offset(new Vector2(1, 0)));
            Assert.That(Hands.TryPickupAnyHand(drone, welder), Is.True);

            var repair = CreateMarker(console, map, new Vector2(4, 0));
            InstallProgram(console, drone, map, new Vector2(-3, 1),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Repair, Net(repair), 3f, 0f, string.Empty));
        });

        await Pair.RunTicksSync(1800);
        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.GetComponent<DamageableComponent>(barricade);
            var remaining = SEntMan.System<DamageableSystem>().GetTotalDamage((barricade, damageable));
            var agent = Agent(drone);
            var held = Hands.EnumerateHeld(drone).ToList();
            Assert.That(remaining.Float(), Is.LessThan(80f),
                $"activity={agent.Activity}, failure={agent.LastFailure}, failures={agent.ProgramFailures}, running={agent.ProgramRunning}, status={agent.Status}, step={agent.StepIndex}, work={agent.WorkTarget}, held={string.Join(',', held)}");
            Assert.That(agent.ProgramFailures, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ReportsVisibleSubject()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-4, 0), new Vector2(-2, 0));
            drone = unit;
            SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(0, 0)));
            var report = CreateMarker(console, map, new Vector2(1, 0));
            InstallProgram(console, drone, map, new Vector2(-4, 1),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Report, Net(report), 6f, 0f, string.Empty));
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Agent(drone).ReportsSent, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ConsoleCommandsAreRateLimited()
    {
        var map = await CreateFloorMap();
        EntityUid console = default;
        await Server.WaitAssertion(() =>
        {
            var pair = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            console = pair.Console;
            var comp = SEntMan.GetComponent<CMUUtilityDroneConsoleComponent>(console);
            var program = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>
                {
                    new(CMUUtilityStepKind.Wait, NetEntity.Invalid, 1f, 0.5f, string.Empty),
                },
                new List<CMUUtilityRuleInfo>(),
                false,
                30f);

            var first = new CMUUtilityConsoleCommandMessage(CMUUtilityConsoleCommand.SaveProgram, program);
            Assert.That(Consoles.HandleConsoleCommand(console, console, first), Is.True);
            Assert.That(comp.Program.Steps.Count, Is.EqualTo(1));

            var second = new CMUUtilityConsoleCommandMessage(CMUUtilityConsoleCommand.SaveProgram, program);
            Assert.That(Consoles.HandleConsoleCommand(console, console, second), Is.False);
            Assert.That(comp.RejectedCommands, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task MarkerMustBeAnchoredAndLinkedBeforePrinting()
    {
        var map = await CreateFloorMap();
        EntityUid console = default;
        EntityUid marker = default;
        await Server.WaitAssertion(() =>
        {
            var pair = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            console = pair.Console;
            marker = SEntMan.SpawnEntity("CMUUtilityTaskMarker", map.GridCoords.Offset(new Vector2(2, 0)));

            Assert.That(Consoles.TryLinkMarker(console, marker, console, out var unanchoredError), Is.False);
            Assert.That(unanchoredError, Is.Not.Empty);

            Assert.That(Transforms.AnchorEntity(marker), Is.True);
            Assert.That(Consoles.TryLinkMarker(console, marker, console, out var linkError), Is.True, linkError);

            var program = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>
                {
                    new(CMUUtilityStepKind.MoveTo, Net(marker), 4f, 0f, string.Empty),
                },
                new List<CMUUtilityRuleInfo>(),
                false,
                30f);
            Assert.That(Drones.ValidateProgram(console, program, out _, out _), Is.True);

            Assert.That(Transforms.Unanchor(marker), Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(marker).Anchored, Is.False);
            Assert.That(Drones.ValidateProgram(console, program, out _, out var anchorError), Is.False);
            Assert.That(anchorError, Is.Not.Empty);

            Assert.That(Transforms.AnchorEntity(marker), Is.True);
            Assert.That(Consoles.TryPrintProgramBoard(console, default, program, out _, out var printError), Is.True, printError);
        });
    }

    [Test]
    public async Task MarkerLinkingUsesMultitoolInMarkerThenConsoleOrder()
    {
        var map = await CreateFloorMap();
        EntityUid console = default;
        EntityUid marker = default;
        EntityUid user = default;
        EntityUid multitool = default;
        await Server.WaitAssertion(() =>
        {
            var pair = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            console = pair.Console;
            marker = SEntMan.SpawnEntity("CMUUtilityTaskMarker", map.GridCoords.Offset(new Vector2(2, 0)));
            user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(-3, 1)));
            multitool = SEntMan.SpawnEntity("CMMultitool", map.GridCoords.Offset(new Vector2(-3, 1)));

            var unanchored = new InteractUsingEvent(user, multitool, marker, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(marker, unanchored);
            Assert.That(unanchored.Handled, Is.True);
            Assert.That(SEntMan.HasComponent<CMUUtilityMarkerLinkerComponent>(multitool), Is.False);

            Assert.That(Transforms.AnchorEntity(marker), Is.True);
            var selectMarker = new InteractUsingEvent(user, multitool, marker, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(marker, selectMarker);
            Assert.That(selectMarker.Handled, Is.True);
            Assert.That(
                SEntMan.GetComponent<CMUUtilityMarkerLinkerComponent>(multitool).PendingMarker,
                Is.EqualTo(marker));

            var selectConsole = new InteractUsingEvent(user, multitool, console, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(console, selectConsole);
            Assert.That(selectConsole.Handled, Is.True);
            Assert.That(
                SEntMan.GetComponent<CMUUtilityDroneConsoleComponent>(console).Markers,
                Does.Contain(marker));
            Assert.That(
                SEntMan.GetComponent<CMUUtilityMarkerLinkerComponent>(multitool).PendingMarker,
                Is.Null);

            var nearby = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(2, 1)));
            var scan = new CMUUtilityConsoleCommandMessage(
                CMUUtilityConsoleCommand.ScanItems,
                new CMUUtilityProgramInfo(),
                SEntMan.GetNetEntity(marker));
            Assert.That(Consoles.HandleConsoleCommand(console, user, scan), Is.True);
            Assert.That(
                SEntMan.GetComponent<CMUUtilityDroneConsoleComponent>(console).ScanItems,
                Does.Contain(nearby));
        });
    }

    [Test]
    public async Task DamageRuleSpeaksConfiguredMessage()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        const string message = "Hull impact detected. Structural integrity nominal.";
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            InstallProgramWithRules(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                new List<CMUUtilityRuleInfo>
                {
                    new(CMUUtilityTrigger.OnDamaged, CMUUtilityRuleAction.Report, message),
                },
                new CMUUtilityStepInfo(CMUUtilityStepKind.Wait, NetEntity.Invalid, 1f, 30f, string.Empty));

            var recorder = Server.System<CMUUtilitySpeechRecorderSystem>();
            recorder.Messages.Clear();
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(1) } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(
                drone,
                damage,
                ignoreResistances: true);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var recorder = Server.System<CMUUtilitySpeechRecorderSystem>();
            var agent = Agent(drone);
            Assert.That(recorder.Messages, Does.Contain(message),
                $"reports={agent.ReportsSent}, pending={agent.PendingTrigger}, status={agent.Status}, activity={agent.Activity}, running={agent.ProgramRunning}");
        });
    }

    [Test]
    public async Task FetchesConfiguredTargetInsteadOfNearestItem()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid target = default;
        EntityUid decoy = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            decoy = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(2, 0)));
            target = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(6, 0)));

            InstallProgram(console, drone, map, new Vector2(-3, 1),
                new CMUUtilityStepInfo(
                    CMUUtilityStepKind.Fetch,
                    NetEntity.Invalid,
                    3f,
                    0f,
                    string.Empty,
                    Net(target)));
        });

        await Pair.RunTicksSync(600);
        await Server.WaitAssertion(() =>
        {
            var held = Hands.EnumerateHeld(drone).ToList();
            Assert.That(held, Does.Contain(target));
            Assert.That(held, Does.Not.Contain(decoy));
            Assert.That(Agent(drone).ProgramFailures, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task PullsTargetRaisesPullTriggersAndReleasesIt()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid target = default;
        const string startedMessage = "Towing began.";
        const string stoppedMessage = "Towing released.";
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            target = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(4, 0)));
            InstallProgramWithRules(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                new List<CMUUtilityRuleInfo>
                {
                    new(CMUUtilityTrigger.OnStartedPulling, CMUUtilityRuleAction.Report, startedMessage),
                    new(CMUUtilityTrigger.OnStoppedPulling, CMUUtilityRuleAction.Report, stoppedMessage),
                },
                new CMUUtilityStepInfo(
                    CMUUtilityStepKind.Pull,
                    NetEntity.Invalid,
                    4f,
                    0f,
                    string.Empty,
                    Net(target)),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Wait, NetEntity.Invalid, 1f, 4f, string.Empty),
                new CMUUtilityStepInfo(CMUUtilityStepKind.StopPull, NetEntity.Invalid, 1f, 0f, string.Empty));

            Server.System<CMUUtilitySpeechRecorderSystem>().Messages.Clear();
        });

        await Pair.RunTicksSync(900);
        await Server.WaitAssertion(() =>
        {
            var pulling = SEntMan.System<PullingSystem>();
            Assert.That(Agent(drone).ProgramFailures, Is.EqualTo(0), Agent(drone).LastFailure);
            Assert.That(pulling.GetPulling(drone), Is.Null);
            var recorder = Server.System<CMUUtilitySpeechRecorderSystem>();
            Assert.That(recorder.Messages, Does.Contain(startedMessage));
            Assert.That(recorder.Messages, Does.Contain(stoppedMessage));
        });
    }

    [Test]
    public async Task CombatUtilityDroneEnablesGunUseAndCivilianRemainsRestricted()
    {
        var map = await CreateFloorMap();
        await Server.WaitAssertion(() =>
        {
            var civilian = SEntMan.SpawnEntity("CMUUtilityDrone", map.GridCoords);
            var combat = SEntMan.SpawnEntity(
                "CMUUtilityDroneCombat",
                map.GridCoords.Offset(new Vector2(1, 0)));
            var civilianSynth = SEntMan.GetComponent<SynthComponent>(civilian);
            var combatSynth = SEntMan.GetComponent<SynthComponent>(combat);
            var civilianAndroid = SEntMan.GetComponent<CMUDroneAndroidComponent>(civilian);
            var combatAndroid = SEntMan.GetComponent<CMUDroneAndroidComponent>(combat);
            Assert.Multiple(() =>
            {
                Assert.That(civilianSynth.CanUseGuns, Is.False);
                Assert.That(combatSynth.CanUseGuns, Is.True);
                Assert.That(civilianAndroid.SupportsCombatModule, Is.False);
                Assert.That(combatAndroid.SupportsCombatModule, Is.True);
            });
        });
    }

    [Test]
    public async Task TextScriptReportsHealthAndLifeState()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        var expectedHealth = string.Empty;
        var expectedPercent = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var subject = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(2, 0)));
            var subjectName = SEntMan.GetComponent<MetaDataComponent>(subject).EntityName;
            SEntMan.System<DamageableSystem>().SetDamage(
                (subject, SEntMan.GetComponent<DamageableComponent>(subject)),
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(7) } });

            var thresholds = SEntMan.GetComponent<MobThresholdsComponent>(subject);
            var maxHealth = thresholds.Thresholds.Keys.Last().Double();
            var current = Math.Max(0, maxHealth - 7);
            expectedHealth =
                $"HP {current.ToString("0.###", CultureInfo.InvariantCulture)}/" +
                $"{maxHealth.ToString("0.###", CultureInfo.InvariantCulture)}";
            expectedPercent =
                $"P {(current / maxHealth * 100).ToString("0.###", CultureInfo.InvariantCulture)}";

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                $$"""
                let target = person("{{subjectName}}")
                say "HP " + health(target) + "/" + maxhealth(target)
                say "P " + healthpercent(target)
                say "A " + alive(target) + " D " + dead(target) + " C " + critical(target)
                stop
                """);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(messages, Does.Contain(expectedHealth), string.Join(" | ", messages));
            Assert.That(messages, Does.Contain(expectedPercent), string.Join(" | ", messages));
            Assert.That(messages, Does.Contain("A true D false C false"), string.Join(" | ", messages));
        });
    }

    [Test]
    public async Task TextScriptReadsDeadStateAfterTargetDies()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid subject = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            subject = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(2, 0)));
            var subjectName = SEntMan.GetComponent<MetaDataComponent>(subject).EntityName;
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                $$"""
                let target = person("{{subjectName}}")
                wait 2
                say "D " + dead(target) + " A " + alive(target) + " H " + health(target)
                stop
                """);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.GetComponent<DamageableComponent>(subject);
            var thresholds = SEntMan.GetComponent<MobThresholdsComponent>(subject);
            var maxHealth = thresholds.Thresholds.Keys.Last().Double();
            SEntMan.System<DamageableSystem>().SetDamage(
                (subject, damageable),
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(maxHealth + 10) } });
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(messages, Does.Contain("D true A false H 0"), string.Join(" | ", messages));
        });
    }

    [Test]
    public async Task TextScriptReloadsMagazineAndReportsAmmo()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(
                map,
                new Vector2(-3, 0),
                new Vector2(0, 0),
                "CMUUtilityDroneCombat");
            drone = unit;
            var backpack = SEntMan.SpawnEntity(
                "CMBackpackMarine",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(
                SEntMan.System<InventorySystem>().TryEquip(drone, backpack, "back", force: true),
                Is.True);
            var gun = SEntMan.SpawnEntity(
                "RMCWeaponRifleM54C",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(Hands.TryPickupAnyHand(drone, gun), Is.True);
            var magazine = SEntMan.SpawnEntity(
                "CMMagazineRifleM54C",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(
                SEntMan.System<SharedStorageSystem>().Insert(
                    backpack,
                    magazine,
                    out _,
                    out _,
                    drone),
                Is.True);

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                reload
                say "Ammo " + ammo() + "/" + maxammo()
                stop
                """);
        });

        await Pair.RunTicksSync(120);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Ammo ", StringComparison.OrdinalIgnoreCase) &&
                    !message.StartsWith("Ammo 0/", StringComparison.OrdinalIgnoreCase) &&
                    !message.StartsWith("Ammo -1", StringComparison.OrdinalIgnoreCase)),
                Is.True,
                string.Join(" | ", messages));
        });
    }

    [Test]
    public async Task TextScriptFightDefeatsNearbyHostile()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid hostile = default;
        string hostileName = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(
                map,
                new Vector2(-3, 0),
                new Vector2(0, 0),
                "CMUUtilityDroneCombat");
            drone = unit;
            var backpack = SEntMan.SpawnEntity(
                "CMBackpackMarine",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(
                SEntMan.System<InventorySystem>().TryEquip(drone, backpack, "back", force: true),
                Is.True);
            var gun = SEntMan.SpawnEntity(
                "RMCWeaponRifleM54C",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(Hands.TryPickupAnyHand(drone, gun), Is.True);
            var magazine = SEntMan.SpawnEntity(
                "CMMagazineRifleM54C",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(
                SEntMan.System<SharedStorageSystem>().Insert(
                    backpack,
                    magazine,
                    out _,
                    out _,
                    drone),
                Is.True);

            hostile = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(5, 0)));
            hostileName = SEntMan.GetComponent<MetaDataComponent>(hostile).EntityName;
            var factions = SEntMan.EnsureComponent<NpcFactionMemberComponent>(hostile);
            factions.Factions.Clear();
            SEntMan.System<NpcFactionSystem>().AddFaction(hostile, "THREAT");

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                say "Hostiles " + hostilecount(12)
                fight
                say "Done"
                stop
                """);
        });

        for (var second = 0; second < 15; second++)
            await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(messages, Does.Contain("Hostiles 1"), string.Join(" | ", messages));
            Assert.That(messages, Does.Contain("Done"), string.Join(" | ", messages));

            var damageable = SEntMan.GetComponent<DamageableComponent>(hostile);
            var damage = SEntMan.System<DamageableSystem>()
                .GetTotalDamage((hostile, damageable))
                .Double();
            var mob = SEntMan.GetComponent<MobStateComponent>(hostile);
            Assert.That(
                damage > 0 || !SEntMan.System<MobStateSystem>().IsAlive(hostile, mob),
                Is.True,
                $"Hostile '{hostileName}' was not engaged (damage {damage}).");
        });
    }

    [Test]
    public async Task TextScriptRepairsSelfWhenNoHostiles()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(
                map,
                new Vector2(-3, 0),
                new Vector2(0, 0),
                "CMUUtilityDroneCombat");
            drone = unit;
            Assert.That(
                SEntMan.GetComponent<SynthComponent>(drone).SelfRepairTime,
                Is.EqualTo(TimeSpan.FromSeconds(12)));
            SEntMan.System<DamageableSystem>().SetDamage(
                (drone, SEntMan.GetComponent<DamageableComponent>(drone)),
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(10) } });
            var welder = SEntMan.SpawnEntity(
                "CMWelder",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(Hands.TryPickupAnyHand(drone, welder), Is.True);

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                repair self
                say "HP " + health() + "/" + maxhealth()
                stop
                """);
        });

        for (var second = 0; second < 25; second++)
            await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(
                agent.ProgramsCompleted,
                Is.EqualTo(1),
                $"activity={agent.Activity}, running={agent.ProgramRunning}, paused={agent.ProgramPaused}, status={agent.Status}");
            var damage = SEntMan.System<DamageableSystem>()
                .GetTotalDamage((drone, SEntMan.GetComponent<DamageableComponent>(drone)))
                .Double();
            Assert.That(damage, Is.EqualTo(0), "The drone did not finish self-repair.");
        });
    }

    [Test]
    public async Task TextScriptRepairsDamagedAllyDrone()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid ally = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(
                map,
                new Vector2(-3, 0),
                new Vector2(0, 0),
                "CMUUtilityDroneCombat");
            drone = unit;
            ally = SEntMan.SpawnEntity(
                "CMUUtilityDroneCombat",
                map.GridCoords.Offset(new Vector2(2, 0)));
            SEntMan.System<DamageableSystem>().SetDamage(
                (ally, SEntMan.GetComponent<DamageableComponent>(ally)),
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(10) } });
            var welder = SEntMan.SpawnEntity(
                "CMWelder",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(Hands.TryPickupAnyHand(drone, welder), Is.True);

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                repair ally within 5
                say "Done"
                stop
                """);
        });

        for (var second = 0; second < 15; second++)
            await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var damage = SEntMan.System<DamageableSystem>()
                .GetTotalDamage((ally, SEntMan.GetComponent<DamageableComponent>(ally)))
                .Double();
            Assert.That(damage, Is.EqualTo(0), "The ally drone was not repaired.");
        });
    }

    [Test]
    public async Task TextScriptRepairRefusesWhenHostileNearby()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(
                map,
                new Vector2(-3, 0),
                new Vector2(0, 0),
                "CMUUtilityDroneCombat");
            drone = unit;
            SEntMan.System<DamageableSystem>().SetDamage(
                (drone, SEntMan.GetComponent<DamageableComponent>(drone)),
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(10) } });
            var welder = SEntMan.SpawnEntity(
                "CMWelder",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(Hands.TryPickupAnyHand(drone, welder), Is.True);

            var hostile = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(4, 0)));
            var factions = SEntMan.EnsureComponent<NpcFactionMemberComponent>(hostile);
            factions.Factions.Clear();
            SEntMan.System<NpcFactionSystem>().AddFaction(hostile, "THREAT");

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                repair self
                say "Damage " + damage()
                stop
                """);
        });

        await Pair.RunTicksSync(300);

        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var damage = SEntMan.System<DamageableSystem>()
                .GetTotalDamage((drone, SEntMan.GetComponent<DamageableComponent>(drone)))
                .Double();
            Assert.That(damage, Is.EqualTo(10), "The drone repaired itself while a hostile was nearby.");
        });
    }

    [Test]
    public async Task TextScriptMovesLoopsAndSpeaks()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid marker = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            marker = CreateMarker(console, map, new Vector2(4, 0));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                let x = 0
                while x < 3 {
                  say "tick"
                  move marker 1
                  wait 1
                  set x = x + 1
                }
                """);
        });

        await Pair.RunTicksSync(900);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var targetPosition = SEntMan.GetComponent<TransformComponent>(marker).Coordinates;
            var dronePosition = SEntMan.GetComponent<TransformComponent>(drone).Coordinates;
            Assert.That(dronePosition.TryDistance(SEntMan, targetPosition, out var distance), Is.True);
            Assert.That(distance, Is.LessThan(2.5f));
            Assert.That(Server.System<CMUUtilitySpeechRecorderSystem>().Messages, Does.Contain("Tick"));
        });
    }

    [Test]
    public async Task TextScriptSupportsRepeatAndIfElse()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                let x = 0
                repeat 3 {
                  set x = x + 1
                }
                if (x == 3) {
                  say "Three"
                } else {
                  say "Wrong"
                }
                """);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            Assert.That(Server.System<CMUUtilitySpeechRecorderSystem>().Messages, Does.Contain("Three"));
        });
    }

    [Test]
    public async Task TextScriptReportsPeopleInRadius()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        string subjectName = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var subject = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            subjectName = SEntMan.GetComponent<MetaDataComponent>(subject).EntityName;
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                say "People: " + people(8)
                """);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(
                Server.System<CMUUtilitySpeechRecorderSystem>().Messages.Any(
                    message => message.Contains(subjectName, StringComparison.OrdinalIgnoreCase)),
                Is.True);
        });
    }

    [Test]
    public async Task TextScriptReadsHandsEquipmentBackpackAndWorldInfo()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid heldCrowbar = default;
        string subjectName = string.Empty;
        string aimTargetName = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var backpack = SEntMan.SpawnEntity(
                "CMBackpackMarine",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(
                SEntMan.System<InventorySystem>().TryEquip(drone, backpack, "back", force: true),
                Is.True);
            var backpackItem = SEntMan.SpawnEntity(
                "Screwdriver",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(
                SEntMan.System<SharedStorageSystem>().Insert(backpack, backpackItem, out _, out _, drone),
                Is.True);

            heldCrowbar = SEntMan.SpawnEntity(
                "Crowbar",
                map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(Hands.TryPickup(drone, heldCrowbar, "left"), Is.True);
            SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(4, 0)));

            var subject = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(2, 0)));
            subjectName = SEntMan.GetComponent<MetaDataComponent>(subject).EntityName;
            var aimTarget = SEntMan.SpawnEntity(
                "Medkit",
                map.GridCoords.Offset(new Vector2(3, 0)));
            aimTargetName = SEntMan.GetComponent<MetaDataComponent>(aimTarget).EntityName;
            var gunItem = SEntMan.SpawnEntity(
                "Crowbar",
                map.GridCoords.Offset(new Vector2(2, 1)));
            var gun = SEntMan.EnsureComponent<GunComponent>(gunItem);
            SEntMan.System<GunSystem>().SwapTarget((gunItem, gun), aimTarget);
            Assert.That(Hands.TryPickup(subject, gunItem), Is.True);
            SEntMan.System<DamageableSystem>().SetDamage(
                (subject, SEntMan.GetComponent<DamageableComponent>(subject)),
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(7) } });

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                $$"""
                say "People: " + people(8)
                say "Items: " + items(8)
                say "Around: " + entities(8)
                say "Hands: " + hands()
                say "Worn: " + worn()
                say "Backpack: " + backpacknames()
                say "Backpack items: " + backpackitems()
                say "Damage: " + damage(person("{{subjectName}}"))
                say "Aim: " + name(aimtarget(person("{{subjectName}}")))
                """);
        });

        await Pair.RunTicksSync(900);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Any(message => message.Contains(subjectName, StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Items:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("Crowbar", StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Hands:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("Crowbar", StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Worn:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("back:", StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Backpack:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("Screwdriver", StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Backpack items:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("Screwdriver", StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Damage:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("7", StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Aim:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains(aimTargetName, StringComparison.OrdinalIgnoreCase)),
                Is.True,
                string.Join(" | ", messages));
        });
    }

    [Test]
    public async Task TextScriptTakesFromBackpackAndSwitchesHands()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid crowbar = default;
        EntityUid backpack = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            backpack = SEntMan.SpawnEntity("CMBackpackMarine", map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(drone, backpack, "back", force: true), Is.True);
            crowbar = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(
                SEntMan.System<SharedStorageSystem>().Insert(backpack, crowbar, out _, out _, drone),
                Is.True);

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                take from backpack "crowbar" into left
                say name(hand("left"))
                switch right
                put in backpack hand("left")
                """);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(Hands.EnumerateHeld(drone), Does.Not.Contain(crowbar));
            Assert.That(Hands.GetActiveHand(drone), Is.EqualTo("right"));
            Assert.That(
                SEntMan.GetComponent<StorageComponent>(backpack).Container.ContainedEntities,
                Does.Contain(crowbar));
        });
    }

    [Test]
    public async Task TextScriptPullsAndReleasesNativeItemSelector()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(4, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                pull item("crowbar", 8)
                say "Pulling: " + name(pulling())
                wait 2
                release
                """);
        });

        await Pair.RunTicksSync(600);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(SEntMan.System<PullingSystem>().GetPulling(drone), Is.Null);
            Assert.That(
                Server.System<CMUUtilitySpeechRecorderSystem>().Messages.Any(
                    message => message.StartsWith("Pulling:", StringComparison.OrdinalIgnoreCase) &&
                               message.Contains("Crowbar", StringComparison.OrdinalIgnoreCase)),
                Is.True);
        });
    }

    [Test]
    public async Task TextScriptTakesAndPullsItemByName()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(4, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                take "Crowbar"
                drop
                pull "Crowbar"
                wait 2
                release
                """);
        });

        await Pair.RunTicksSync(600);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(Hands.EnumerateHeld(drone), Is.Empty);
            Assert.That(SEntMan.System<PullingSystem>().GetPulling(drone), Is.Null);
        });
    }

    [Test]
    public async Task TextScriptItemSearchRejectsPartialWords()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(4, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                take item "row" within 8
                """);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(1));
            Assert.That(agent.LastFailure, Is.Not.Empty);
        });
    }

    [Test]
    public async Task TextScriptReportsTheAttackerAfterDamage()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        string attackerName = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var attacker = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(2, 0)));
            attackerName = SEntMan.GetComponent<MetaDataComponent>(attacker).EntityName;
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                while true {
                  if damagetaken() {
                    if exists(attacker()) {
                      say "Attacker: " + name(attacker())
                    } else {
                      say "Attacker: unknown"
                    }
                  }
                  wait 1
                }
                """);

            Server.System<CMUUtilitySpeechRecorderSystem>().Messages.Clear();
            SEntMan.System<DamageableSystem>().TryChangeDamage(
                drone,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(1) } },
                ignoreResistances: true,
                origin: attacker);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("Attacker:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains(attackerName, StringComparison.OrdinalIgnoreCase)),
                Is.True,
                string.Join(" | ", messages));
        });
    }

    [Test]
    public async Task TextScriptDropsItemWhenBackpackCannotFit()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid crowbar = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            crowbar = SEntMan.SpawnEntity(
                "Crowbar",
                map.GridCoords.Offset(new Vector2(3, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                take "Crowbar" into left
                switch left
                if backpackfits(hand("left")) {
                  put in backpack hand("left")
                } else {
                  drop
                }
                """);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            Assert.That(Hands.EnumerateHeld(drone), Does.Not.Contain(crowbar));
        });
    }

    [Test]
    public async Task TextScriptEventHandlerUsesVariableChangedByMainProgram()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid attacker = default;
        string attackerName = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            CreateMarker(console, map, new Vector2(4, 0));
            CreateMarker(console, map, new Vector2(8, 0));
            attacker = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(2, 2)));
            attackerName = SEntMan.GetComponent<MetaDataComponent>(attacker).EntityName;

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                let hit_message = "1"

                on damaged {
                  if exists(attacker()) {
                    say hit_message + ":" + name(attacker())
                  } else {
                    say hit_message
                  }
                }

                main {
                  while true {
                    move marker 2
                    set hit_message = "2"
                    wait 1
                  }
                }
                """);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<DamageableSystem>().TryChangeDamage(
                drone,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(1) } },
                ignoreResistances: true,
                origin: attacker);
        });

        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("1:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains(attackerName, StringComparison.OrdinalIgnoreCase)),
                Is.True,
                string.Join(" | ", messages));
        });

        await Pair.RunTicksSync(600);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<DamageableSystem>().TryChangeDamage(
                drone,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(1) } },
                ignoreResistances: true,
                origin: attacker);
        });

        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Any(message =>
                    message.StartsWith("2:", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains(attackerName, StringComparison.OrdinalIgnoreCase)),
                Is.True,
                string.Join(" | ", messages));
            Assert.That(Drones.StopProgram(drone, out var error), Is.True, error);
        });
    }

    [Test]
    public async Task TextScriptMainBlockMovesByCoordinatesAndDirection()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                main {
                  move coords 2 0
                  move up 2
                  say "done"
                }
                """);
        });

        await Pair.RunTicksSync(600);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var position = SEntMan.GetComponent<TransformComponent>(drone).Coordinates.Position;
            Assert.That(Vector2.Distance(position, new Vector2(2, 2)), Is.LessThan(1.2f));
        });
    }

    [Test]
    public async Task TextScriptInspectsNearestPersonAndWornItems()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var subject = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(2, 0)));
            var backpack = SEntMan.SpawnEntity(
                "CMBackpackMarine",
                map.GridCoords.Offset(new Vector2(2, 0)));
            SEntMan.System<MetaDataSystem>().SetEntityName(backpack, "wey backpack");
            Assert.That(
                SEntMan.System<InventorySystem>().TryEquip(subject, backpack, "back", force: true),
                Is.True);

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                main {
                  let who = nearestperson(8)
                  if exists(who) {
                    say inspectperson(who)
                    if worncontains(who, "wey") {
                      say "привет сотрудник"
                    } else {
                      say "привет незнакомец"
                    }
                  }
                }
                """);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Any(message => message.Contains("wey backpack", StringComparison.OrdinalIgnoreCase)),
                Is.True,
                string.Join(" | ", messages));
            Assert.That(messages, Does.Contain("Привет сотрудник"));
        });
    }

    [Test]
    public async Task UtilityDroneHasTwoNormalStrippableHands()
    {
        var map = await CreateFloorMap();
        await Server.WaitAssertion(() =>
        {
            var (_, drone) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            var hands = SEntMan.GetComponent<HandsComponent>(drone);
            Assert.That(hands.SortedHands, Is.EquivalentTo(new[] { "left", "right" }));
            Assert.That(hands.CanBeStripped, Is.True);
        });
    }

    [Test]
    public async Task TextScriptFindsItemByVisibleRussianName()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var crowbar = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(2, 0)));
            SEntMan.System<MetaDataSystem>().SetEntityName(crowbar, "аварийный лом");
            Server.System<CMUUtilitySpeechRecorderSystem>().Messages.Clear();

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                main {
                  let found = itemdisplay("лом", 8)
                  say "Имя: " + itemname(found)
                  say "ID: " + itemid(found)
                  stop
                }
                """);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var recorder = Server.System<CMUUtilitySpeechRecorderSystem>();
            Assert.That(recorder.Messages, Does.Contain("Имя: аварийный лом"));
            Assert.That(recorder.Messages, Does.Contain("ID: Crowbar"));
        });
    }

    [Test]
    public async Task TextScriptUseSelfAndUseOnTarget()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid lighter = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            lighter = SEntMan.SpawnEntity("Lighter", map.GridCoords.Offset(new Vector2(0, 1)));
            Assert.That(Hands.TryPickupAnyHand(drone, lighter), Is.True);
            SEntMan.SpawnEntity("Paper", map.GridCoords.Offset(new Vector2(3, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                main {
                  use self
                  use on "Paper"
                }
                """);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            Assert.That(SEntMan.System<ItemToggleSystem>().IsActivated(lighter), Is.True);
        });
    }

    [Test]
    public async Task TextScriptHeardPhraseInterruptsPatrolAndMovesToMarker()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid marker = default;
        EntityUid speaker = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            marker = CreateMarker(console, map, new Vector2(4, 0));
            CreateMarker(console, map, new Vector2(8, 0));
            speaker = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(2, 0)));

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                on heard "код" {
                  say "heard"
                  move marker 1
                  stop
                }

                main {
                  while true {
                    move marker 2
                    wait 1
                  }
                }
                """);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(
                speaker,
                "код",
                InGameICChatType.Speak,
                false);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(
                Server.System<CMUUtilitySpeechRecorderSystem>().Messages.Any(
                    message => message.Equals("Heard", StringComparison.OrdinalIgnoreCase)),
                Is.True);
            var position = SEntMan.GetComponent<TransformComponent>(drone).Coordinates;
            var markerPosition = SEntMan.GetComponent<TransformComponent>(marker).Coordinates;
            Assert.That(position.TryDistance(SEntMan, markerPosition, out var distance), Is.True);
            Assert.That(distance, Is.LessThan(1.5f));
        });
    }

    [Test]
    public async Task TextScriptHeardPhraseRestartsPatrol()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid speaker = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            CreateMarker(console, map, new Vector2(3, 0));
            CreateMarker(console, map, new Vector2(6, 0));
            speaker = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(1, 0)));

            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                on heard "код" {
                  say "heard"
                  move marker 1
                  restart
                }

                main {
                  while true {
                    say "patrol"
                    move marker 2
                    wait 1
                  }
                }
                """);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(
                speaker,
                "код",
                InGameICChatType.Speak,
                false);
        });

        await Pair.RunTicksSync(360);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramRunning, Is.True);
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Count(message =>
                    message.Equals("Patrol", StringComparison.OrdinalIgnoreCase)),
                Is.GreaterThanOrEqualTo(2),
                string.Join(" | ", messages));
        });
    }

    [Test]
    public async Task TextScriptGotoLoopsInsideMain()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                main {
                  let x = 0

                  loop:
                    set x = x + 1
                    say "tick"
                    if x < 3 {
                      goto loop
                    }
                    say "done"
                }
                """);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(agent.ProgramsCompleted, Is.EqualTo(1));
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(
                messages.Count(message =>
                    message.Equals("Tick", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(3),
                string.Join(" | ", messages));
            Assert.That(messages, Does.Contain("Done"));
        });
    }

    [Test]
    public async Task TextScriptRejectsUnknownGotoLabelBeforePrinting()
    {
        var map = await CreateFloorMap();
        await Server.WaitAssertion(() =>
        {
            var (console, _) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            var program = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>(),
                new List<CMUUtilityRuleInfo>(),
                false,
                30f)
            {
                Script = """
                main {
                  goto missing
                }
                """,
            };

            Assert.That(
                Consoles.TryPrintProgramBoard(console, default, program, out _, out var error),
                Is.False);
            Assert.That(error, Is.Not.Empty);
        });
    }

    [Test]
    public async Task TextScriptHandlerGotoMainLabel()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid speaker = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            speaker = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(1, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                on heard "код" {
                  goto main.alarm
                }

                main {
                  patrol:
                    say "patrol"
                    wait 5
                    goto patrol

                  alarm:
                    say "alarm"
                    stop
                }
                """);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(
                speaker,
                "код",
                InGameICChatType.Speak,
                false);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(messages, Does.Contain("Alarm"));
        });
    }

    [Test]
    public async Task TextScriptHandlerReturnResumesMain()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid speaker = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            speaker = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(1, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                on heard "код" {
                  say "heard"
                  return
                  say "after"
                }

                main {
                  while true {
                    say "patrol"
                    wait 1
                  }
                }
                """);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(
                speaker,
                "код",
                InGameICChatType.Speak,
                false);
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            var messages = Server.System<CMUUtilitySpeechRecorderSystem>().Messages;
            Assert.That(messages, Does.Contain("Heard"));
            Assert.That(messages, Does.Not.Contain("After"));
            Assert.That(
                messages.Count(message =>
                    message.Equals("Patrol", StringComparison.OrdinalIgnoreCase)),
                Is.GreaterThanOrEqualTo(2),
                string.Join(" | ", messages));
        });
    }

    [Test]
    public async Task TextScriptHandlerCanTakeItem()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid speaker = default;
        EntityUid crowbar = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            speaker = SEntMan.SpawnEntity(
                "CMMobHuman",
                map.GridCoords.Offset(new Vector2(1, 0)));
            crowbar = SEntMan.SpawnEntity(
                "Crowbar",
                map.GridCoords.Offset(new Vector2(2, 0)));
            InstallScript(
                console,
                drone,
                map,
                new Vector2(-3, 1),
                """
                on heard "код" {
                  take "Crowbar" into left
                  stop
                }

                main {
                  while true {
                    wait 5
                  }
                }
                """);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(
                speaker,
                "код",
                InGameICChatType.Speak,
                false);
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(Hands.EnumerateHeld(drone), Does.Contain(crowbar));
        });
    }

    [Test]
    public async Task InvalidTextScriptIsRejectedBeforePrinting()
    {
        var map = await CreateFloorMap();
        await Server.WaitAssertion(() =>
        {
            var (console, _) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            var program = new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>(),
                new List<CMUUtilityRuleInfo>(),
                false,
                30f)
            {
                Script = "while true {",
            };

            Assert.That(
                Consoles.TryPrintProgramBoard(console, default, program, out _, out var error),
                Is.False);
            Assert.That(error, Is.Not.Empty);
        });
    }

    [Test]
    public async Task TextScriptBoardIgnoresLegacyRules()
    {
        var map = await CreateFloorMap();
        await Server.WaitAssertion(() =>
        {
            var (console, _) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            var board = PrintScriptBoard(
                console,
                "say \"text path\"",
                new List<CMUUtilityRuleInfo>
                {
                    new(CMUUtilityTrigger.OnDamaged, CMUUtilityRuleAction.Return),
                });

            var boardComp = SEntMan.GetComponent<CMUUtilityDroneProgramBoardComponent>(board);
            Assert.That(boardComp.Script, Is.EqualTo("say \"text path\""));
            Assert.That(boardComp.Program.Steps, Is.Empty);
            Assert.That(boardComp.Program.Rules, Is.Empty);
        });
    }

    [Test]
    public async Task PauseResumeStopAndRestartControlsWork()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            InstallProgram(console, drone, map, new Vector2(-3, 1),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Wait, NetEntity.Invalid, 1f, 30f, string.Empty));
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            Assert.That(agent.ProgramRunning, Is.True);

            Assert.That(Drones.PauseProgram(drone, out var pauseError), Is.True, pauseError);
            Assert.That(agent.ProgramPaused, Is.True);

            Assert.That(Drones.ResumeProgram(drone, out var resumeError), Is.True, resumeError);
            Assert.That(agent.ProgramPaused, Is.False);

            Assert.That(Drones.StopProgram(drone, out var stopError), Is.True, stopError);
            Assert.That(agent.ProgramRunning, Is.False);

            Assert.That(Drones.TryRestartProgram(drone, out var restartError), Is.True, restartError);
            Assert.That(agent.ProgramRunning, Is.True);
            Assert.That(agent.StepIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ProgrammableModeClearsTabletBinding()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid tablet = default;
        EntityUid user = default;
        await Server.WaitAssertion(() =>
        {
            drone = SEntMan.SpawnEntity("CMUUtilityDrone", map.GridCoords.Offset(new Vector2(0, 0)));
            tablet = SEntMan.SpawnEntity("CMUDroneControlTablet", map.GridCoords.Offset(new Vector2(1, 0)));
            user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(2, 0)));

            var droneComp = SEntMan.GetComponent<CMUDroneAndroidComponent>(drone);
            var tabletComp = SEntMan.GetComponent<CMUDroneControlTabletComponent>(tablet);
            var operatorComp = SEntMan.AddComponent<CMUDroneOperatorComponent>(user);

            droneComp.Operator = user;
            droneComp.Tablet = tablet;
            tabletComp.Operator = user;
            tabletComp.LinkedDrone = drone;
            operatorComp.Drone = drone;
            operatorComp.Tablet = tablet;
            operatorComp.ControlledDrone = drone;

            Assert.That(Drones.TrySetProgrammableMode(drone, true, out var error), Is.True, error);

            Assert.That(droneComp.Operator, Is.Null);
            Assert.That(droneComp.Tablet, Is.Null);
            Assert.That(tabletComp.Operator, Is.Null);
            Assert.That(tabletComp.LinkedDrone, Is.Null);
            Assert.That(operatorComp.Drone, Is.Null);
            Assert.That(operatorComp.Tablet, Is.Null);
            Assert.That(operatorComp.ControlledDrone, Is.Null);
        });
    }

    [Test]
    public async Task BoardInstallationRequiresModePortAndTools()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid board = default;
        EntityUid installer = default;
        EntityUid multitool = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            var marker = CreateMarker(console, map, new Vector2(2, 0));
            board = PrintBoard(console,
                new CMUUtilityStepInfo(CMUUtilityStepKind.MoveTo, Net(marker), 4f, 0f, string.Empty));
            installer = CreateInstaller(map, new Vector2(-3, 1), board);
            multitool = Hands.EnumerateHeld(installer).First(item => item != board);

            Assert.That(Drones.CanInstallProgramBoard(drone, board, installer, out var modeError), Is.False);
            Assert.That(modeError, Is.Not.Empty);

            Assert.That(Drones.TrySetProgrammableMode(drone, true, out var enableError), Is.True, enableError);
            Assert.That(Drones.CanInstallProgramBoard(drone, board, installer, out var lockError), Is.False);
            Assert.That(lockError, Is.Not.Empty);

            Assert.That(Drones.TrySetBoardPortLocked(drone, false, out var unlockError), Is.True, unlockError);
            Assert.That(Drones.CanInstallProgramBoard(drone, board, installer, out var readyError), Is.True, readyError);

            Assert.That(Hands.TryDrop(installer, board), Is.True);
            Assert.That(Drones.CanInstallProgramBoard(drone, board, installer, out var handError), Is.False);
            Assert.That(handError, Is.Not.Empty);

            Assert.That(Hands.TryPickup(installer, board), Is.True);
            Assert.That(Hands.TryDrop(installer, multitool), Is.True);
            Assert.That(Drones.CanInstallProgramBoard(drone, board, installer, out var toolError), Is.False);
            Assert.That(toolError, Is.Not.Empty);
        });
    }

    [Test]
    public async Task FollowStepTracksBoardInstaller()
    {
        var map = await CreateFloorMap();
        EntityUid drone = default;
        EntityUid installer = default;
        await Server.WaitAssertion(() =>
        {
            var (console, unit) = SpawnPair(map, new Vector2(-3, 0), new Vector2(0, 0));
            drone = unit;
            installer = InstallProgram(console, drone, map, new Vector2(-3, 1),
                new CMUUtilityStepInfo(CMUUtilityStepKind.Follow, NetEntity.Invalid, 1f, 5f, string.Empty));
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            Transforms.SetCoordinates(installer, map.GridCoords.Offset(new Vector2(8, 0)));
        });

        await Pair.RunTicksSync(300);
        await Server.WaitAssertion(() =>
        {
            var agent = Agent(drone);
            var dronePosition = Transforms.GetWorldPosition(drone);
            var installerPosition = Transforms.GetWorldPosition(installer);
            Assert.That(agent.ProgramFailures, Is.EqualTo(0), agent.LastFailure);
            Assert.That(Vector2.Distance(dronePosition, installerPosition), Is.LessThan(3f));
        });
    }

    [Test]
    public async Task ConsoleStateSerializesAndBuildsTheWindow()
    {
        var state = new CMUUtilityDroneConsoleState(
            new List<CMUUtilityMarkerInfo>
            {
                new(default, true),
            },
            new CMUUtilityProgramInfo(
                new List<CMUUtilityStepInfo>
                {
                    new(CMUUtilityStepKind.MoveTo, NetEntity.Invalid, 4f, 0f, string.Empty),
                    new(CMUUtilityStepKind.Fetch, NetEntity.Invalid, 4f, 0f, string.Empty, new NetEntity(42)),
                    new(CMUUtilityStepKind.Wait, NetEntity.Invalid, 1f, 2f, string.Empty),
                },
                new List<CMUUtilityRuleInfo>
                {
                    new(CMUUtilityTrigger.OnDamaged, CMUUtilityRuleAction.Report, "Custom damage phrase."),
                    new(CMUUtilityTrigger.OnStartedPulling, CMUUtilityRuleAction.Return),
                },
                true,
                30f),
            "Program draft saved.",
            string.Empty,
            new List<CMUUtilityItemInfo>
            {
                new(new NetEntity(42), "Test item", true, true),
            },
            default,
            "say \"test\"");

        byte[] payload = default!;
        await Server.WaitAssertion(() =>
        {
            var serializer = Server.ResolveDependency<IRobustSerializer>();
            using var stream = new MemoryStream();
            serializer.Serialize(stream, state);
            payload = stream.ToArray();
        });

        Assert.That(payload, Is.Not.Empty);
        await Client.WaitAssertion(() =>
        {
            var serializer = Client.ResolveDependency<IRobustSerializer>();
            using var stream = new MemoryStream(payload);
            var deserialized = serializer.Deserialize(stream);
            Assert.That(deserialized, Is.TypeOf<CMUUtilityDroneConsoleState>());
            var window = new CMUUtilityDroneConsoleWindow();
            window.UpdateState((CMUUtilityDroneConsoleState) deserialized);
            window.Dispose();
            var commandWindow = new CMUUtilityDroneCommandWindow();
            commandWindow.Dispose();
        });
    }

    private void InstallProgramOnMaps(
        EntityUid console,
        EntityUid drone,
        EntityUid map,
        Vector2 installerPosition,
        params CMUUtilityStepInfo[] steps)
    {
        var board = PrintBoard(console, steps);
        var installer = SEntMan.SpawnEntity("CMMobHuman", new EntityCoordinates(map, installerPosition));
        Assert.That(Hands.TryPickup(installer, board), Is.True);
        var multitool = SEntMan.SpawnEntity("CMMultitool", new EntityCoordinates(map, installerPosition));
        Assert.That(Hands.TryPickupAnyHand(installer, multitool), Is.True);
        Assert.That(Drones.TrySetProgrammableMode(drone, true, out var modeError), Is.True, modeError);
        Assert.That(Drones.TrySetBoardPortLocked(drone, false, out var portError), Is.True, portError);
        Assert.That(Drones.TryInstallProgramBoard(drone, board, installer, out var error), Is.True, error);
    }

    private sealed class CMUUtilitySpeechRecorderSystem : EntitySystem
    {
        public readonly List<string> Messages = new();

        public override void Initialize()
        {
            SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        }

        private void OnSpoke(EntitySpokeEvent ev)
        {
            Messages.Add(ev.Message);
        }
    }
}
