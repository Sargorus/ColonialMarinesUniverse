using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Research;
using Content.Server.CMU14.ColonyEconomy;
using Content.Server.CMU14.Robotics;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Robotics;
using Content.Shared.CMU14.Yautja;
using Content.Shared.UserInterface;
using Content.Shared._RMC14.Requisitions.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMUCompanyMissionTest : GameTest
{
    [Test]
    public async Task TwoTerminalsShareOneRoboticsOrderPool()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var first = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords);
            var second = SEntMan.SpawnEntity("CMURoboticsOrderConsole", map.GridCoords.Offset(new(2, 0)));

            var pools = Server.System<CMURoboticsOrderPoolSystem>();
            Assert.That(pools.GetPool(second), Is.SameAs(pools.GetPool(first)),
                "both WEYU terminals must draw from the same order pool");
        });
    }

    [Test]
    public async Task RoboticsPointsMissionPaysCompanyValue()
    {
        var map = await Pair.CreateTestMap();
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CorporateConsoleUi.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleAcceptMissionBuiMsg("robotics-points")
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.ActiveMissions.Select(mission => mission.Id), Does.Contain("robotics-points"));
            var active = state.ActiveMissions.Single(mission => mission.Id == "robotics-points");
            Assert.That(active.TimeLimitMinutes, Is.EqualTo(30),
                "the mission limit runs for 30 minutes from acceptance");
            Assert.That(active.Target, Is.GreaterThan(0));
        });

        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ServerResearchDataTerminalSystem>();
            research.UpdateRoboticsPoints(25, "corporate");
        });

        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.ActiveMissions.Select(mission => mission.Id), Does.Not.Contain("robotics-points"));
            Assert.That(state.MissionHistory.Select(mission => mission.Id), Does.Contain("robotics-points"));
            Assert.That(state.CompanyValue, Is.GreaterThan(0),
                "the robotics mission reward is paid in company value");
            Assert.That(state.ChemistryPoints, Is.EqualTo(0),
                "mission rewards must not pay chemistry points");

            var next = state.AvailableMissions.Single(mission => mission.Id == "robotics-points");
            Assert.That(next.Target, Is.GreaterThan(0));
            Assert.That(next.Target, Is.EqualTo(state.MissionHistory
                    .Single(mission => mission.Id == "robotics-points").Target + 3),
                "the repeatable points mission raises its target by 3");
        });
    }

    [Test]
    public async Task PointsMissionCountsOnlyNewPointsEarnedAfterAcceptance()
    {
        var map = await Pair.CreateTestMap();
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);
            console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CorporateConsoleUi.Key,
                    user),
                Is.True);

            SEntMan.System<ServerResearchDataTerminalSystem>().UpdateRoboticsPoints(10, "corporate");
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleAcceptMissionBuiMsg("robotics-points")
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            var active = state!.ActiveMissions.Single(mission => mission.Id == "robotics-points");
            Assert.Multiple(() =>
            {
                Assert.That(active.Progress, Is.Zero);
                Assert.That(active.Target, Is.EqualTo(5));
            });
        });

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<ServerResearchDataTerminalSystem>().UpdateRoboticsPoints(13, "corporate");
        });
        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            var active = state!.ActiveMissions.Single(mission => mission.Id == "robotics-points");
            Assert.That(active.Progress, Is.EqualTo(3));
        });

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<ServerResearchDataTerminalSystem>().UpdateRoboticsPoints(15, "corporate");
        });
        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.MissionHistory.Select(mission => mission.Id), Does.Contain("robotics-points"));
            Assert.That(state.CompanyValue, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ComplexDirectionCannotBeChangedAfterSelection()
    {
        var map = await Pair.CreateTestMap();
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);
            console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CorporateConsoleUi.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleSetDirectionBuiMsg(
                CorporateResearchDirection.Robotics)
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleSetDirectionBuiMsg(
                CorporateResearchDirection.Bioengineering)
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.Direction, Is.EqualTo(CorporateResearchDirection.Robotics),
                "the complex specialization is fixed for the whole round");
        });
    }

    [Test]
    public async Task SimpleScanMissionUsesUniqueOpposingPair()
    {
        var map = await Pair.CreateTestMap();
        EntityUid console = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CorporateConsoleUi.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleAcceptMissionBuiMsg("chemistry-scan-simple")
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        var firstObjective = string.Empty;
        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            var active = state!.ActiveMissions.Single(mission => mission.Id == "chemistry-scan-simple");
            firstObjective = active.Objective;
            Assert.That(firstObjective, Does.Contain(" and "),
                "the active scan task must name both required properties");

            var properties = new Dictionary<string, int>();
            foreach (var property in SProtoMan.GetInstances<ReagentPropertyPrototype>().Values)
            {
                if (firstObjective.Contains(property.LocalizedName))
                    properties[property.ID] = 1;
            }

            Assert.That(properties.Count, Is.GreaterThanOrEqualTo(2));
            var scanned = new CMUChemicalScannedEvent("corporate", "test", properties);
            SEntMan.EventBus.RaiseLocalEvent(console, ref scanned, broadcast: true);
        });
        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.MissionHistory.Select(mission => mission.Id), Does.Contain("chemistry-scan-simple"));
            Assert.That(state.CompanyValue, Is.GreaterThan(0));

            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleAcceptMissionBuiMsg("chemistry-scan-simple")
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            var next = state!.ActiveMissions.Single(mission => mission.Id == "chemistry-scan-simple");
            Assert.That(next.Objective, Is.Not.EqualTo(firstObjective),
                "the round must not repeat the same opposing property pair");
        });
    }

    [Test]
    public async Task YautjaBracerUnlockPaysRepeatableCompanyValue()
    {
        var map = await Pair.CreateTestMap();
        EntityUid console = default;
        EntityUid elevator = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords.Offset(new(2, 0)));
            var elevatorComp = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);
            elevatorComp.Faction = "corporate";
            elevatorComp.Orders.Clear();
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CorporateConsoleUi.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleAcceptMissionBuiMsg("predator-bracer")
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            var elevatorComp = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);
            Assert.That(elevatorComp.Orders, Has.Count.EqualTo(1),
                "accepting the bracer mission ships one recovered bracer to cargo");
            Assert.That(elevatorComp.Orders[0].Crate.Id, Is.EqualTo("CMUCrateSecureYautjaTechnology"));
        });

        await Server.WaitAssertion(() =>
        {
            var bracer = SEntMan.SpawnEntity("CMUYautjaBracer", map.GridCoords);
            var unlocked = new CMUYautjaBracerUnlockedEvent(bracer, EntityUid.Invalid);
            SEntMan.EventBus.RaiseLocalEvent(bracer, ref unlocked);
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.CompanyValue, Is.EqualTo(50),
                "opening a Yautja bracer pays 50 company value");
            Assert.That(state.MissionHistory.Select(mission => mission.Id), Does.Contain("predator-bracer"));
            Assert.That(state.AvailableMissions.Select(mission => mission.Id), Does.Contain("predator-bracer"),
                "the bracer task must be repeatable for every recovered bracer");
        });
    }

    [Test]
    public async Task CompanySupportSpendsCompanyValueAndQueuesAlienTechnology()
    {
        var map = await Pair.CreateTestMap();
        EntityUid console = default;
        EntityUid elevator = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);

            console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            elevator = SEntMan.SpawnEntity("AU14CorporateASRSElevator", map.GridCoords.Offset(new(2, 0)));
            var elevatorComp = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);
            elevatorComp.Faction = "corporate";
            elevatorComp.Orders.Clear();

            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(
                    console,
                    CorporateConsoleUi.Key,
                    user),
                Is.True);
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Server.System<CorporateConsoleSystem>().SetCompanyValue("corporate", 1000);
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.CompanyValue, Is.EqualTo(1000));
        });

        var afterCredit = 0;
        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleRequestCreditBuiMsg
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CorporateConsoleComponent>(console).CorporateBudget,
                Is.GreaterThan(0));
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.CompanyValue, Is.LessThan(1000));
            afterCredit = state.CompanyValue;
        });

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleRequestAlienTechBuiMsg
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            var elevatorComp = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);
            Assert.That(elevatorComp.Orders, Has.Count.EqualTo(1));
            Assert.That(elevatorComp.Orders[0].Crate.Id, Is.EqualTo("CMUCrateSecureYautjaTechnology"));
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.CompanyValue, Is.LessThan(afterCredit));
        });
    }
}
