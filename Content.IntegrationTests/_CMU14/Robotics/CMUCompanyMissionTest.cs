using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Research;
using Content.Server.CMU14.Robotics;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Robotics;
using Content.Shared.UserInterface;
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
    public async Task RoboticsPointsMissionPaysChemistryPoints()
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
            Assert.That(state.ChemistryPoints, Is.GreaterThanOrEqualTo(15),
                "the robotics mission reward is paid in chemistry points");
        });
    }
}
