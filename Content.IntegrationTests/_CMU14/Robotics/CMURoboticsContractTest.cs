using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Robotics;
using Content.Server._RMC14.Requisitions;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared._RMC14.Armor;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Crate;
using Content.Shared.Body.Part;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Medical.Anatomy.BodyParts;
using Content.Shared.CMU14.Requisitions;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Lock;
using Content.Shared.Mobs.Components;
using Content.Shared.Paper;
using Content.Shared.Materials;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMURoboticsContractTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task DroneOnlyAcceptsDroneAndroidLimbs()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var drone = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords);
            var droneArm = SEntMan.SpawnEntity("CMUPartDroneAndroidLeftArm", map.GridCoords);
            var joeArm = SEntMan.SpawnEntity("CMUPartWorkingJoeLeftArm", map.GridCoords);
            var synthArm = SEntMan.SpawnEntity("CMUPartSynthLeftArm", map.GridCoords);
            var prostheticArm = SEntMan.SpawnEntity("CMUPartRoboticLeftArm", map.GridCoords);
            var joe = SEntMan.SpawnEntity("CMUWorkingJoeRepair", map.GridCoords);

            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, drone, droneArm), Is.True);
            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, drone, joeArm), Is.False);
            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, drone, synthArm), Is.False);
            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, drone, prostheticArm), Is.False);

            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, joe, joeArm), Is.True);
            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, joe, synthArm), Is.True);
            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, joe, droneArm), Is.False);

            Assert.That(CMULimbCompatibility.IsWorkingJoeLimb(SEntMan, joeArm), Is.True);
            Assert.That(CMULimbCompatibility.IsWorkingJoeLimb(SEntMan, droneArm), Is.False);
        });
    }

    [Test]
    public async Task DroneAssemblyPartsAreSurgicalLimbsAndOrderMetadataIsCurrent()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var drone = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords);
            var assemblyArm = SEntMan.SpawnEntity("CMUDroneLeftArmAssembly", map.GridCoords);
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var slip = SEntMan.SpawnEntity("CMURoboticsOrderSlip", map.GridCoords);

            Assert.That(SEntMan.HasComponent<CMUDroneAssemblyPartComponent>(assemblyArm), Is.True,
                "the printable drone arm must still fit a drone frame");
            Assert.That(SEntMan.HasComponent<BodyPartComponent>(assemblyArm), Is.True,
                "the printable drone arm must also be a surgical body part");
            Assert.That(CMULimbCompatibility.IsDroneLimb(SEntMan, assemblyArm), Is.True);
            Assert.That(CMULimbCompatibility.CanAttach(SEntMan, drone, assemblyArm), Is.True);

            Assert.That(
                SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console).OrderDuration,
                Is.GreaterThan(TimeSpan.Zero));
            Assert.That(SEntMan.HasComponent<PaperComponent>(slip), Is.True,
                "the order slip must use a CMU/RMC paper prototype");
        });
    }

    [Test]
    public async Task TransportCrateHoldsOneSynthetic()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var crate = SEntMan.SpawnEntity("CMUCrateSyntheticTransport", map.GridCoords);
            Assert.That(SEntMan.HasComponent<CMURoboticsTransportCrateComponent>(crate), Is.True);

            var storage = SEntMan.GetComponent<EntityStorageComponent>(crate);
            Assert.That(storage.Capacity, Is.EqualTo(2));
            Assert.That(storage.MaxSize, Is.GreaterThanOrEqualTo(1f));

            var system = Server.System<CMURoboticsTransportCrateSystem>();
            var drone = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords);
            Assert.That(SEntMan.HasComponent<SynthComponent>(drone), Is.True, "drone must have Synth");
            Assert.That(storage.Open, Is.False, "transport crate must start closed");
            Assert.That(storage.Contents.ContainedEntities.Count, Is.EqualTo(0), "crate must start empty");
            Assert.That(SEntMan.GetComponent<LockComponent>(crate).Locked, Is.False, "crate must start unlocked");
            Assert.That(SEntMan.HasComponent<MobStateComponent>(drone), Is.True, "drone must have MobState");
            Assert.That(storage.Blacklist, Is.Null, "crate storage must not have a blacklist");
            var droneAabb = Server.System<EntityLookupSystem>().GetAABBNoContainer(drone, default, 0);
            Assert.That(droneAabb.Size.X, Is.LessThanOrEqualTo(storage.MaxSize),
                $"drone AABB width {droneAabb.Size.X} must fit max size {storage.MaxSize}");
            Assert.That(droneAabb.Size.Y, Is.LessThanOrEqualTo(storage.MaxSize),
                $"drone AABB height {droneAabb.Size.Y} must fit max size {storage.MaxSize}");
            var insertAttempt = new InsertIntoEntityStorageAttemptEvent(storage.Contents, drone);
            SEntMan.EventBus.RaiseLocalEvent(drone, ref insertAttempt);
            Assert.That(insertAttempt.Cancelled, Is.False, "InsertIntoEntityStorageAttemptEvent must not cancel");
            var crateAttempt = new EntityStorageInsertedIntoAttemptEvent(storage.Contents, drone);
            SEntMan.EventBus.RaiseLocalEvent(crate, ref crateAttempt);
            Assert.That(crateAttempt.Cancelled, Is.False, "EntityStorageInsertedIntoAttemptEvent must not cancel");
            Assert.That(
                Server.System<SharedEntityStorageSystem>().CanInsert(drone, crate, storage),
                Is.True,
                "EntityStorage must accept the drone");
            Assert.That(system.CanStoreSynthetic(crate, drone), Is.True, "crate system must accept the drone");
            Assert.That(
                system.TryStoreSynthetic(crate, EntityUid.Invalid, drone),
                Is.True,
                "a drone must be draggable into the transport crate");
            Assert.That(storage.Contents.Contains(drone), Is.True);

            var second = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords);
            Assert.That(
                system.TryStoreSynthetic(crate, EntityUid.Invalid, second),
                Is.False,
                "the transport crate must hold only one synthetic");

            var tablet = SEntMan.SpawnEntity("CMUDroneControlTablet", map.GridCoords);
            Assert.That(Server.System<SharedEntityStorageSystem>().Insert(tablet, crate), Is.True,
                "the second crate slot must accept a drone control tablet");

            var slip = SEntMan.SpawnEntity("CMURoboticsOrderSlip", map.GridCoords);
            SEntMan.GetComponent<CMURoboticsOrderSlipComponent>(slip).OrderId = 42;

            var slots = Server.System<ItemSlotsSystem>();
            Assert.That(
                slots.TryInsert(crate, "paper_label", slip, null),
                Is.True,
                "the transport crate label slot must accept the robotics order slip");

            var labels = Server.System<LabelSystem>();
            Assert.That(labels.TryGetLabel<CMURoboticsOrderSlipComponent>(crate, out var label), Is.True);
            Assert.That(label!.Value.Comp.OrderId, Is.EqualTo(42));
        });
    }

    [Test]
    public async Task RepairOrderQueuesThreeCratesWithMarkedDamagedUnits()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var elevator = SEntMan.SpawnEntity(
                "AU14CorporateASRSElevator",
                map.GridCoords.Offset(new System.Numerics.Vector2(2, 0)));

            var consoleComp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);
            var elevatorComp = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);

            var order = new CMURoboticsOrder
            {
                Id = 987,
                Type = CMURoboticsOrderType.Repair,
                Target = "CMUDroneAndroidUnarmed",
                RepairTarget = "CMUDroneAndroidUnarmedRepairOrder",
                Amount = 3,
                TargetName = "test drone",
                Reward = 1000,
            };
            consoleComp.AvailableOrders.Clear();
            consoleComp.AvailableOrders.Add(order);

            var message = new CMURoboticsAcceptOrderMessage(order.Id)
            {
                Actor = EntityUid.Invalid,
                UiKey = CMURoboticsOrderConsoleUiKey.Key,
            };
            SEntMan.EventBus.RaiseLocalEvent(console, message);

            Assert.That(consoleComp.ActiveOrder, Is.Not.Null);
            Assert.That(elevatorComp.Orders.Count, Is.EqualTo(3));

            var timing = Server.ResolveDependency<IGameTiming>();
            elevatorComp.Mode = RequisitionsElevatorMode.Raising;
            elevatorComp.NextMode = null;
            elevatorComp.Busy = true;
            elevatorComp.RaiseDelay = TimeSpan.Zero;
            elevatorComp.LowerDelay = TimeSpan.Zero;
            elevatorComp.ToggledAt = timing.CurTime - TimeSpan.FromSeconds(1);

            var requisitions = Server.System<RequisitionsSystem>();
            var steps = 0;
            while (elevatorComp.Mode != RequisitionsElevatorMode.Raised && steps++ < 100)
                requisitions.Update(0f);

            Assert.That(elevatorComp.Mode, Is.EqualTo(RequisitionsElevatorMode.Raised));

            var crates = 0;
            var query = SEntMan.EntityQueryEnumerator<CMURoboticsTransportCrateComponent, EntityStorageComponent>();
            while (query.MoveNext(out _, out _, out var storage))
            {
                crates++;
                Assert.That(storage.Contents.ContainedEntities.Count, Is.EqualTo(1),
                    "a transport crate must arrive with one synthetic inside");

                var contained = storage.Contents.ContainedEntities[0];
                Assert.That(SEntMan.HasComponent<CMURoboticsOrderTargetComponent>(contained), Is.True);
                Assert.That(
                    SEntMan.GetComponent<CMURoboticsOrderTargetComponent>(contained).OrderId,
                    Is.EqualTo(order.Id));
            }

            Assert.That(crates, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ExpiredOrderChargesQuarterOfRewardAsFine()
    {
        var map = await Pair.CreateTestMap();
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var consoleComp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);
            var timing = Server.ResolveDependency<IGameTiming>();

            consoleComp.ActiveOrder = new CMURoboticsOrder
            {
                Id = 5150,
                Type = CMURoboticsOrderType.Build,
                Target = "CMUDroneAndroidUnarmed",
                Amount = 1,
                TargetName = "test drone",
                Reward = 4000,
                Deadline = timing.CurTime - TimeSpan.FromSeconds(1),
            };
        });

        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var corporate = SEntMan.EntityQueryEnumerator<CorporateConsoleComponent>();
            Assert.That(corporate.MoveNext(out _, out var budget), Is.True);
            Assert.That(budget.CorporateBudget, Is.EqualTo(-1000),
                "an expired order must charge 25% of its reward");
            Assert.That(SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console).ActiveOrder, Is.Null);
        });
    }

    [Test]
    public async Task PrintedOrderSlipUsesRealLineBreaks()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var consoleComp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);
            consoleComp.ActiveOrder = new CMURoboticsOrder
            {
                Id = 4242,
                Type = CMURoboticsOrderType.Build,
                Target = "CMUDroneAndroidUnarmed",
                Amount = 1,
                TargetName = "test drone",
                Reward = 3500,
            };

            var print = new CMURoboticsPrintSlipMessage
            {
                Actor = EntityUid.Invalid,
                UiKey = CMURoboticsOrderConsoleUiKey.Key,
            };
            SEntMan.EventBus.RaiseLocalEvent(console, print);

            var query = SEntMan.EntityQueryEnumerator<CMURoboticsOrderSlipComponent, PaperComponent>();
            Assert.That(query.MoveNext(out _, out _, out var paper), Is.True,
                "printing an accepted order must create a paper slip");
            Assert.That(paper.Content, Does.Contain("\n"), "the slip must contain real line breaks");
            Assert.That(paper.Content, Does.Not.Contain("\\n"), "literal \\n must not be printed");
            Assert.That(paper.Content, Does.Contain("[bold]"), "the title should use allowed paper markup");
        });
    }

    [Test]
    public async Task GeneratedRepairOrderShowsFinalRewardImmediately()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var consoleComp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);
            consoleComp.MaxAvailableOrders = 30;
            consoleComp.AvailableOrders.Clear();

            var refresh = new CMURoboticsRefreshOrdersMessage
            {
                Actor = EntityUid.Invalid,
                UiKey = CMURoboticsOrderConsoleUiKey.Key,
            };
            SEntMan.EventBus.RaiseLocalEvent(console, refresh);

            var repair = consoleComp.AvailableOrders.FirstOrDefault(
                order => order.Type == CMURoboticsOrderType.Repair);

            Assert.That(repair, Is.Not.Null, "the console must generate repair orders");
            Assert.That(repair!.RepairBurnDamage, Is.GreaterThan(0));
            Assert.That(repair.RepairBruteDamage, Is.GreaterThan(0));
            Assert.That(repair.RepairMissingLimbs, Is.GreaterThan(0));
            Assert.That(repair.Reward, Is.GreaterThan(2500),
                "the reward shown in the order list must already include the repair bonus");
        });
    }

    [Test]
    public async Task RepairOrderDeliversPreRolledDamageWithoutChangingReward()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords);
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var elevatorComp = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);
            var consoleComp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);

            var order = new CMURoboticsOrder
            {
                Id = 989,
                Type = CMURoboticsOrderType.Repair,
                Target = "CMUDroneAndroidUnarmed",
                RepairTarget = "CMUDroneAndroidUnarmedRepairOrder",
                Amount = 1,
                TargetName = "test drone",
                Reward = 5000,
                RepairBurnDamage = 50,
                RepairBruteDamage = 40,
                RepairMissingLimbs = 2,
                Elevator = elevator,
            };
            consoleComp.ActiveOrder = order;

            var requisitions = Server.System<RequisitionsSystem>();
            var entry = new RequisitionsEntry
            {
                Crate = "CMUCrateSyntheticTransport",
                CMUOrderId = order.Id,
                Entities = { "CMUDroneAndroidUnarmedRepairOrder" },
            };
            Assert.That(
                requisitions.TryAddElevatorOrders((elevator, elevatorComp), new List<RequisitionsEntry> { entry }),
                Is.True);

            var timing = Server.ResolveDependency<IGameTiming>();
            elevatorComp.Mode = RequisitionsElevatorMode.Raising;
            elevatorComp.NextMode = null;
            elevatorComp.Busy = true;
            elevatorComp.RaiseDelay = TimeSpan.Zero;
            elevatorComp.LowerDelay = TimeSpan.Zero;
            elevatorComp.ToggledAt = timing.CurTime - TimeSpan.FromSeconds(1);

            var steps = 0;
            while (elevatorComp.Mode != RequisitionsElevatorMode.Raised && steps++ < 100)
                requisitions.Update(0f);

            Assert.That(elevatorComp.Mode, Is.EqualTo(RequisitionsElevatorMode.Raised));
            Assert.That(order.Reward, Is.EqualTo(5000),
                "delivering a repair crate must not recalculate the accepted reward");

            EntityUid? unit = null;
            var query = SEntMan.EntityQueryEnumerator<CMURoboticsTransportCrateComponent, EntityStorageComponent>();
            while (query.MoveNext(out _, out _, out var storage))
            {
                if (storage.Contents.ContainedEntities.Count > 0)
                    unit = storage.Contents.ContainedEntities[0];
            }

            Assert.That(unit, Is.Not.Null);
            var marker = SEntMan.GetComponent<CMURoboticsOrderTargetComponent>(unit!.Value);
            Assert.That(marker.OrderId, Is.EqualTo(order.Id));
            Assert.That(marker.InitialMissingLimbs, Is.EqualTo(2));
            Assert.That(marker.InitialDamage, Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.New(90)));

            var damageable = Server.System<Content.Shared.Damage.Systems.DamageableSystem>();
            Assert.That(damageable.GetTotalDamage(unit.Value),
                Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.New(90)));
            Assert.That(CountMissingExternalLimbs(unit.Value), Is.EqualTo(2));
        });
    }

    private int CountMissingExternalLimbs(EntityUid unit)
    {
        var body = SEntMan.GetComponent<Content.Shared.Body.BodyComponent>(unit);
        var bodySystem = Server.System<Content.Shared.Body.Systems.SharedBodySystem>();
        var missing = 0;
        foreach (var type in new[] { BodyPartType.Arm, BodyPartType.Leg })
        {
            var count = 0;
            foreach (var _ in bodySystem.GetBodyChildrenOfType(unit, type, body))
                count++;

            missing += Math.Max(0, 2 - count);
        }

        return missing;
    }

    [Test]
    public async Task WorkingJoeRepairCrateArrivesWithOutfit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var elevator = SEntMan.SpawnEntity(
                "AU14CorporateASRSElevator",
                map.GridCoords.Offset(new System.Numerics.Vector2(2, 0)));
            var consoleComp = SComp<CMURoboticsOrderConsoleComponent>(console);
            var elevatorComp = SComp<RequisitionsElevatorComponent>(elevator);
            var repairTarget = consoleComp.RepairTargets.Single(target => target.WorkingJoe);

            Assert.That(repairTarget.Outfits, Has.Count.GreaterThan(1),
                "Working Joe repair orders must have more than one outfit to choose from.");

            var order = new CMURoboticsOrder
            {
                Id = 991,
                Type = CMURoboticsOrderType.Repair,
                Target = repairTarget.Prototype,
                RepairTarget = repairTarget.RepairPrototype!.Value,
                WorkingJoe = true,
                Outfits = repairTarget.Outfits,
                Amount = 1,
                TargetName = "test joe",
                Reward = 1000,
                Elevator = elevator,
            };
            consoleComp.ActiveOrder = order;

            var requisitions = Server.System<RequisitionsSystem>();
            var entry = new RequisitionsEntry
            {
                Crate = "CMUCrateSyntheticTransport",
                CMUOrderId = order.Id,
                Entities = { "CMUWorkingJoeRepairOrder" },
            };
            Assert.That(
                requisitions.TryAddElevatorOrders((elevator, elevatorComp), new List<RequisitionsEntry> { entry }),
                Is.True);

            var timing = Server.ResolveDependency<IGameTiming>();
            elevatorComp.Mode = RequisitionsElevatorMode.Raising;
            elevatorComp.NextMode = null;
            elevatorComp.Busy = true;
            elevatorComp.RaiseDelay = TimeSpan.Zero;
            elevatorComp.LowerDelay = TimeSpan.Zero;
            elevatorComp.ToggledAt = timing.CurTime - TimeSpan.FromSeconds(1);

            var steps = 0;
            while (elevatorComp.Mode != RequisitionsElevatorMode.Raised && steps++ < 100)
                requisitions.Update(0f);

            Assert.That(elevatorComp.Mode, Is.EqualTo(RequisitionsElevatorMode.Raised));

            EntityUid? joe = null;
            var query = SEntMan.EntityQueryEnumerator<CMURoboticsTransportCrateComponent, EntityStorageComponent>();
            while (query.MoveNext(out _, out _, out var storage))
            {
                if (storage.Contents.ContainedEntities.Count > 0)
                    joe = storage.Contents.ContainedEntities[0];
            }

            Assert.That(joe, Is.Not.Null);
            var marker = SComp<CMURoboticsOrderTargetComponent>(joe!.Value);
            Assert.That(marker.Outfit, Is.Not.Empty, "the Joe must arrive wearing an outfit");

            var inventory = Server.System<InventorySystem>();
            foreach (var (slot, prototype) in marker.Outfit)
            {
                Assert.That(inventory.TryGetSlotEntity(joe.Value, slot, out var equipped), Is.True,
                    $"the Joe must keep {prototype} in the {slot} slot");
                Assert.That(
                    SEntMan.GetComponent<MetaDataComponent>(equipped!.Value).EntityPrototype?.ID,
                    Is.EqualTo(prototype.Id));
            }
        });
    }

    [Test]
    public async Task RepairOrderPaysWhenPaperIsAttachedToLoadedCrate()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords);
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var crate = SEntMan.SpawnEntity("CMUCrateSyntheticTransport", map.GridCoords);
            var drone = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords);

            var marker = SEntMan.AddComponent<CMURoboticsOrderTargetComponent>(drone);
            marker.OrderId = 555;

            var storage = Server.System<SharedEntityStorageSystem>();
            Assert.That(storage.Insert(drone, crate), Is.True);

            var slip = SEntMan.SpawnEntity("CMURoboticsOrderSlip", map.GridCoords);
            SEntMan.GetComponent<CMURoboticsOrderSlipComponent>(slip).OrderId = 555;
            var slots = Server.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(crate, "paper_label", slip, null), Is.True);

            var consoleComp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);
            consoleComp.ActiveOrder = new CMURoboticsOrder
            {
                Id = 555,
                Type = CMURoboticsOrderType.Repair,
                Target = "CMUDroneAndroidUnarmed",
                Amount = 1,
                TargetName = "test drone",
                Reward = 1000,
                Elevator = elevator,
            };

            var budget = SEntMan.GetComponent<CorporateConsoleComponent>(
                SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords));
            var before = budget.CorporateBudget;
            var attempt = new CMURequisitionsElevatorLoweringAttemptEvent(elevator);
            SEntMan.EventBus.RaiseLocalEvent(elevator, ref attempt);

            Assert.That(consoleComp.ActiveOrder, Is.Null);
            Assert.That(SEntMan.EntityExists(drone), Is.True,
                "submitting a contract must not delete the repaired unit");
            Assert.That(budget.CorporateBudget, Is.EqualTo(before + 1000),
                "a valid submission must pay the corporate affairs console account");
        });
    }

    [Test]
    public async Task MissingOutfitElementsReduceRepairReward()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords);
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var crate = SEntMan.SpawnEntity("CMUCrateSyntheticTransport", map.GridCoords);
            var joe = SEntMan.SpawnEntity("CMUWorkingJoeRepairOrder", map.GridCoords);
            var marker = SEntMan.AddComponent<CMURoboticsOrderTargetComponent>(joe);
            marker.OrderId = 556;
            marker.Outfit["jumpsuit"] = "CMJumpsuitAverageJoe";
            marker.Outfit["shoes"] = "CMBootsBlack";

            Assert.That(Server.System<SharedEntityStorageSystem>().Insert(joe, crate), Is.True);

            var slip = SEntMan.SpawnEntity("CMURoboticsOrderSlip", map.GridCoords);
            SComp<CMURoboticsOrderSlipComponent>(slip).OrderId = 556;
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(crate, "paper_label", slip, null), Is.True);

            var consoleComp = SComp<CMURoboticsOrderConsoleComponent>(console);
            consoleComp.ActiveOrder = new CMURoboticsOrder
            {
                Id = 556,
                Type = CMURoboticsOrderType.Repair,
                Target = "CMUWorkingJoeRepair",
                RepairTarget = "CMUWorkingJoeRepairOrder",
                Amount = 1,
                TargetName = "test joe",
                Reward = 1000,
                Elevator = elevator,
                WorkingJoe = true,
            };

            var budget = SComp<CorporateConsoleComponent>(
                SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords));
            var before = budget.CorporateBudget;
            var attempt = new CMURequisitionsElevatorLoweringAttemptEvent(elevator);
            SEntMan.EventBus.RaiseLocalEvent(elevator, ref attempt);

            Assert.That(consoleComp.ActiveOrder, Is.Null);
            Assert.That(budget.CorporateBudget, Is.EqualTo(before + 900),
                "two missing outfit elements must deduct 10% of the reward");
            Assert.That(consoleComp.LastPenalty, Is.EqualTo(100));
            Assert.That(consoleComp.LastMissingElements, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task OutfitPenaltyCanConsumeEntireReward()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords);
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var crate = SEntMan.SpawnEntity("CMUCrateSyntheticTransport", map.GridCoords);
            var joe = SEntMan.SpawnEntity("CMUWorkingJoeRepairOrder", map.GridCoords);
            var marker = SEntMan.AddComponent<CMURoboticsOrderTargetComponent>(joe);
            marker.OrderId = 557;
            for (var i = 0; i < 20; i++)
                marker.Outfit[$"missing-slot-{i}"] = "CMJumpsuitAverageJoe";

            Assert.That(Server.System<SharedEntityStorageSystem>().Insert(joe, crate), Is.True);

            var slip = SEntMan.SpawnEntity("CMURoboticsOrderSlip", map.GridCoords);
            SComp<CMURoboticsOrderSlipComponent>(slip).OrderId = 557;
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(crate, "paper_label", slip, null), Is.True);

            var consoleComp = SComp<CMURoboticsOrderConsoleComponent>(console);
            consoleComp.ActiveOrder = new CMURoboticsOrder
            {
                Id = 557,
                Type = CMURoboticsOrderType.Repair,
                Target = "CMUWorkingJoeRepair",
                RepairTarget = "CMUWorkingJoeRepairOrder",
                Amount = 1,
                TargetName = "test joe",
                Reward = 1000,
                Elevator = elevator,
                WorkingJoe = true,
            };

            var budget = SComp<CorporateConsoleComponent>(
                SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords));
            var before = budget.CorporateBudget;
            var attempt = new CMURequisitionsElevatorLoweringAttemptEvent(elevator);
            SEntMan.EventBus.RaiseLocalEvent(elevator, ref attempt);

            Assert.That(consoleComp.ActiveOrder, Is.Null);
            Assert.That(budget.CorporateBudget, Is.EqualTo(before),
                "the penalty must be capped at the full reward");
            Assert.That(consoleComp.LastPenalty, Is.EqualTo(1000));
            Assert.That(consoleComp.LastMissingElements, Is.EqualTo(20));
        });
    }

    [Test]
    public async Task BuildOrderRequiresControlTabletAndPaysCorporateConsole()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords);
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var crate = SEntMan.SpawnEntity("CMUCrateSyntheticTransport", map.GridCoords);
            var drone = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords);
            var tablet = SEntMan.SpawnEntity("CMUDroneControlTablet", map.GridCoords);

            var storage = Server.System<SharedEntityStorageSystem>();
            Assert.That(storage.Insert(drone, crate), Is.True);
            Assert.That(storage.Insert(tablet, crate), Is.True);

            var slip = SEntMan.SpawnEntity("CMURoboticsOrderSlip", map.GridCoords);
            SEntMan.GetComponent<CMURoboticsOrderSlipComponent>(slip).OrderId = 777;
            var slots = Server.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(crate, "paper_label", slip, null), Is.True);

            var consoleComp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);
            consoleComp.ActiveOrder = new CMURoboticsOrder
            {
                Id = 777,
                Type = CMURoboticsOrderType.Build,
                Target = "CMUDroneAndroidUnarmed",
                Amount = 1,
                TargetName = "test drone",
                Reward = 3500,
                Elevator = elevator,
            };

            var budget = SEntMan.GetComponent<CorporateConsoleComponent>(
                SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords));
            var before = budget.CorporateBudget;
            var attempt = new CMURequisitionsElevatorLoweringAttemptEvent(elevator);
            SEntMan.EventBus.RaiseLocalEvent(elevator, ref attempt);

            Assert.That(consoleComp.ActiveOrder, Is.Null);
            Assert.That(SEntMan.EntityExists(drone), Is.True);
            Assert.That(budget.CorporateBudget, Is.EqualTo(before + 3500));
        });
    }

    [Test]
    public async Task FabricatorCrateIsSealedCaseWithoutCell()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var crate = SEntMan.SpawnEntity("CMUCrateRoboticsFabricator", map.GridCoords);
            Assert.That(SEntMan.HasComponent<CrateOpenableComponent>(crate), Is.True,
                "the fabricator crate must unpack when pried");
            Assert.That(SEntMan.HasComponent<EntityStorageComponent>(crate), Is.False,
                "the fabricator must arrive in a sealed case, not a container crate");

            var fabricator = SEntMan.SpawnEntity("CMURoboticsFabricator", map.GridCoords);
            var material = SEntMan.GetComponent<MaterialStorageComponent>(fabricator);
            Assert.That(material.Storage, Is.Empty,
                "the cargo fabricator must arrive with no materials loaded");

            var containers = Server.System<SharedContainerSystem>();
            Assert.That(
                containers.TryGetContainer(fabricator, "cmu_robotics_fabricator_cell", out var cell),
                Is.True);
            Assert.That(cell!.ContainedEntities.Count, Is.EqualTo(0),
                "the cargo fabricator must arrive without a fuel cell");
        });
    }

    [Test]
    public async Task ElevatorQueueRefusesOverCapacityOrders()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords);
            var component = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);
            var system = Server.System<RequisitionsSystem>();
            var entity = (elevator, component);

            var capacity = system.GetElevatorRemainingOrderSlots(entity);
            Assert.That(capacity, Is.GreaterThan(0));

            var entries = new List<RequisitionsEntry>();
            for (var i = 0; i < capacity; i++)
                entries.Add(new RequisitionsEntry { Crate = "CMUCrateSyntheticTransport" });

            Assert.That(system.TryAddElevatorOrders(entity, entries), Is.True);
            Assert.That(system.GetElevatorRemainingOrderSlots(entity), Is.EqualTo(0));

            var extra = new List<RequisitionsEntry>
            {
                new() { Crate = "CMUCrateSyntheticTransport" },
            };
            Assert.That(system.TryAddElevatorOrders(entity, extra), Is.False);
            Assert.That(component.Orders.Count, Is.EqualTo(capacity));
        });
    }

    [Test]
    public async Task CivilianDroneHasNoDefaultSkills()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var civilian = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords);
            var combat = SEntMan.SpawnEntity("CMUDroneAndroidCombat", map.GridCoords);

            var civilianSkills = SEntMan.GetComponent<SkillsComponent>(civilian);
            Assert.That(civilianSkills.Skills, Is.Empty,
                "a fresh civilian drone must not inherit Working Joe skills");
            Assert.That(civilianSkills.Preset, Is.Null,
                "the drone must not reapply the Working Joe skill preset");

            var combatSkills = SEntMan.GetComponent<SkillsComponent>(combat);
            Assert.That(combatSkills.Skills.Keys, Does.Contain("RMCSkillFirearms"));
            Assert.That(combatSkills.Skills.Keys, Does.Contain("RMCSkillCqc"));
        });
    }

    [Test]
    public async Task CombatDroneHasNoDefaultArmor()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var combat = SEntMan.SpawnEntity("CMUDroneAndroidCombat", map.GridCoords);
            Assert.That(SEntMan.HasComponent<CMArmorComponent>(combat), Is.False,
                "the combat drone must not start with built-in armour");
        });
    }

    [Test]
    public async Task CorporateCatalogSellsCombatFrameWithLimitedStock()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("AU14CorporateASRSConsole", map.GridCoords);
            var catalog = SEntMan.GetComponent<RequisitionsComputerComponent>(console);
            var entries = catalog.Categories.SelectMany(category => category.Entries).ToList();

            var frame = entries.SingleOrDefault(entry => entry.Crate == "CMUCrateCombatDroneFrame");
            Assert.That(frame, Is.Not.Null, "the corporate catalog must sell a combat drone frame");
            Assert.That(frame!.Cost, Is.GreaterThan(0));
            Assert.That(frame.MaxStock, Is.GreaterThanOrEqualTo(1));
            Assert.That(frame.StartingStock, Is.InRange(0, frame.MaxStock));
            Assert.That(frame.StockReplenishDelay, Is.GreaterThan(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task RoboticsConsoleHasNoCombatBuildOrders()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var comp = SEntMan.GetComponent<CMURoboticsOrderConsoleComponent>(console);

            Assert.That(comp.BuildTargets.Select(target => target.Prototype.Id), Does.Not.Contain("CMUDroneAndroidCombat"),
                "combat drones must not be offered as production orders");
            Assert.That(comp.RepairTargets.Select(target => target.Prototype.Id), Does.Contain("CMUDroneAndroidCombat"),
                "combat drones should still be repairable");
        });
    }

    [Test]
    public async Task FabricatorCanPrintAllDroneSkillBoards()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var recipes = SProtoMan.Index<LatheRecipePackPrototype>("CMURoboticsFabricatorRecipes")
                .Recipes
                .Select(recipe => recipe.Id)
                .ToHashSet();
            foreach (var recipe in new[]
                     {
                         "CMURoboticsModuleCombat",
                         "CMURoboticsModuleMedical",
                         "CMURoboticsModuleEngineering",
                         "CMURoboticsModulePolice",
                     })
            {
                Assert.That(recipes, Does.Contain(recipe),
                    $"the robotics fabricator must print {recipe}");
            }

            Assert.That(recipes, Does.Not.Contain("CMURoboticsFrameCombat"),
                "the combat drone frame must only be supplied through cargo");
        });
    }
}
