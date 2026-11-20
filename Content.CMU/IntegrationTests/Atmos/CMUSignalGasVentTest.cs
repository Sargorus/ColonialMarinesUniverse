using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Atmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.Atmos;

[TestFixture]
public sealed class CMUSignalGasVentTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task OpenSignalReleasesInsertedTankGas()
    {
        var map = await Pair.CreateTestMap();
        EntityUid vent = default;
        EntityUid tank = default;
        GasTankComponent tankComp = default!;

        await Server.WaitAssertion(() =>
        {
            vent = SEntMan.SpawnEntity("CMUSignalGasVent", map.GridCoords);
            tank = SEntMan.SpawnEntity("OxygenTank", map.GridCoords);

            tankComp = SEntMan.GetComponent<GasTankComponent>(tank);
            tankComp.Air.AdjustMoles(Gas.Oxygen, 10f);

            var slots = Server.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(vent, "tank", tank, null), Is.True,
                "the vent must accept a gas tank");

            var signal = new SignalReceivedEvent("Open");
            SEntMan.EventBus.RaiseLocalEvent(vent, ref signal);

            var ventComp = SEntMan.GetComponent<SignalGasVentComponent>(vent);
            Assert.That(ventComp.Enabled, Is.True, "the Open signal must open the vent");
        });

        // Let the real atmos device loop process the open vent.
        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(tankComp.Air.TotalMoles, Is.LessThan(10f),
                "an open vent must move gas out of the inserted tank through the atmos update loop");
        });
    }

    [Test]
    public async Task OpenSignalWithoutTankDoesNothing()
    {
        var map = await Pair.CreateTestMap();
        EntityUid vent = default;

        await Server.WaitAssertion(() =>
        {
            vent = SEntMan.SpawnEntity("CMUSignalGasVent", map.GridCoords);

            var signal = new SignalReceivedEvent("Open");
            SEntMan.EventBus.RaiseLocalEvent(vent, ref signal);

            Assert.That(SEntMan.GetComponent<SignalGasVentComponent>(vent).Enabled, Is.False,
                "a vent without a tank must not open");
        });
    }

    [Test]
    public async Task CloseSignalStopsRelease()
    {
        var map = await Pair.CreateTestMap();
        EntityUid vent = default;
        EntityUid tank = default;

        await Server.WaitAssertion(() =>
        {
            vent = SEntMan.SpawnEntity("CMUSignalGasVent", map.GridCoords);
            tank = SEntMan.SpawnEntity("OxygenTank", map.GridCoords);
            SEntMan.GetComponent<GasTankComponent>(tank).Air.AdjustMoles(Gas.Oxygen, 10f);
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(vent, "tank", tank, null), Is.True);

            var open = new SignalReceivedEvent("Open");
            SEntMan.EventBus.RaiseLocalEvent(vent, ref open);
            Assert.That(SEntMan.GetComponent<SignalGasVentComponent>(vent).Enabled, Is.True);

            var close = new SignalReceivedEvent("Close");
            SEntMan.EventBus.RaiseLocalEvent(vent, ref close);
            Assert.That(SEntMan.GetComponent<SignalGasVentComponent>(vent).Enabled, Is.False);
        });
    }

    [Test]
    public async Task CanisterIsRejected()
    {
        var map = await Pair.CreateTestMap();
        EntityUid vent = default;
        EntityUid canister = default;

        await Server.WaitAssertion(() =>
        {
            vent = SEntMan.SpawnEntity("CMUSignalGasVent", map.GridCoords);
            canister = SEntMan.SpawnEntity("OxygenCanister", map.GridCoords);

            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(vent, "tank", canister, null), Is.False,
                "the vent must only accept gas tanks, not large canisters");
        });
    }
}
