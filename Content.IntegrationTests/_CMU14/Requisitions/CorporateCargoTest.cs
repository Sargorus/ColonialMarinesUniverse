using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.CMU14.Requisitions;

[TestFixture]
public sealed class CorporateCargoTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task MapInitStorageOpenDoesNotDeployCargoCrate()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var crate = SEntMan.SpawnEntity("CMUCrateRoboticsOrderConsole", map.GridCoords);
            var storage = Server.System<SharedEntityStorageSystem>();
            storage.OpenStorage(crate);
            Assert.That(SEntMan.EntityExists(crate), Is.True,
                "The map-init open/close cycle must not deploy and delete a cargo crate.");
            Assert.That(SEntMan.IsQueuedForDeletion(crate), Is.False,
                "The map-init open/close cycle must not queue the cargo crate for deletion.");
        });
    }

    [Test]
    public async Task CorporateConsoleIsCorporateAndDeliversPaidCrate()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("AU14CorporateASRSConsole", map.GridCoords);
            var elevator = SEntMan.SpawnEntity(
                "AU14CorporateASRSElevator",
                map.GridCoords.Offset(new System.Numerics.Vector2(2, 0)));

            var computer = SEntMan.GetComponent<RequisitionsComputerComponent>(console);
            Assert.That(computer.Faction, Is.EqualTo("corporate"));

            var component = SEntMan.GetComponent<RequisitionsElevatorComponent>(elevator);
            component.RoundStartFreeCrateGiven = true;
            component.Orders.Add(new RequisitionsEntry { Crate = "CMUCrateRoboticsOrderConsole" });
            component.Mode = RequisitionsElevatorMode.Raising;
            component.NextMode = null;
            component.Busy = true;
            component.RaiseDelay = TimeSpan.Zero;
            component.LowerDelay = TimeSpan.Zero;
            component.ToggledAt = Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);

            var system = Server.System<RequisitionsSystem>();
            system.Update(0f);
            var steps = 0;
            while (component.Mode != RequisitionsElevatorMode.Raised && steps++ < 50)
                system.Update(0f);

            Assert.That(component.Mode, Is.EqualTo(RequisitionsElevatorMode.Raised));
            Assert.That(component.Orders, Is.Empty);

            var delivered = 0;
            var crates = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (crates.MoveNext(out _, out var metadata, out var transform))
            {
                if (metadata.EntityPrototype?.ID != "CMUCrateRoboticsOrderConsole")
                    continue;

                Assert.That(transform.MapID, Is.EqualTo(map.MapId));
                delivered++;
            }

            Assert.That(delivered, Is.EqualTo(1));
        });
    }
}
