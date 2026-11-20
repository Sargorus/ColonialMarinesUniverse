using Content.IntegrationTests.Fixtures;
using Content.Server.Turrets;
using Content.Shared.Access.Components;
using Content.Shared.Turrets;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.Turrets;

[TestFixture]
public sealed class CMUTurretToggleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task ManualToggleRequiresOneSecondAction()
    {
        var map = await Pair.CreateTestMap();
        EntityUid turret = default;
        EntityUid user = default;

        await Server.WaitAssertion(() =>
        {
            turret = SEntMan.SpawnEntity("WeaponEnergyTurretAI", map.GridCoords);
            user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);

            // The base system checks access on completion. This test only cares about timing.
            SEntMan.RemoveComponent<AccessReaderComponent>(turret);

            var turrets = Server.System<DeployableTurretSystem>();
            Assert.That(turrets.TryStartToggle(turret, user), Is.True, "the toggle action must start");
            Assert.That(SEntMan.GetComponent<DeployableTurretComponent>(turret).Enabled, Is.False,
                "the turret must not toggle instantly");
        });

        await RunSeconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<DeployableTurretComponent>(turret).Enabled, Is.False,
                "the turret must still be inactive halfway through the action");
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<DeployableTurretComponent>(turret).Enabled, Is.True,
                "the turret must activate after the one second action completes");
        });
    }
}
