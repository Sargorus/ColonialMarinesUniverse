using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Research;
using Content.Server.CMU14.Robotics;
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
            Assert.That(
                state.ActiveMissions.Single(mission => mission.Id == "robotics-points").TimeLimitMinutes,
                Is.EqualTo(60),
                "the mission limit runs from acceptance, not from a fixed round minute");
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
            Assert.That(state.CompanyValue, Is.EqualTo(15),
                "the robotics mission reward is paid in company value");
            Assert.That(state.ChemistryPoints, Is.EqualTo(0),
                "mission rewards must not pay chemistry points");
        });
    }

    [Test]
    public async Task YautjaBracerUnlockPaysRepeatableCompanyValue()
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
            SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleAcceptMissionBuiMsg("predator-bracer")
            {
                Actor = EntityUid.Invalid,
                UiKey = CorporateConsoleUi.Key,
            });
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            var bracer = SEntMan.SpawnEntity("CMUYautjaBracer", map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(bracer, new CMUYautjaBracerUnlockedEvent(bracer, EntityUid.Invalid));
        });
        await Pair.RunUntilSynced();

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.CompanyValue, Is.EqualTo(1));
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

        // Earn company value through a mission first.
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
            SEntMan.System<ServerResearchDataTerminalSystem>().UpdateRoboticsPoints(25, "corporate");
        });
        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.CompanyValue, Is.EqualTo(15));
        });

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
                Is.EqualTo(5000));
            Assert.That(
                SEntMan.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<CorporateConsoleBuiState>(console, CorporateConsoleUi.Key, out var state),
                Is.True);
            Assert.That(state!.CompanyValue, Is.EqualTo(5));
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
            Assert.That(state!.CompanyValue, Is.EqualTo(0));
        });
    }
}
