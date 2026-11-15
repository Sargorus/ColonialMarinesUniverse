using System.Collections.Generic;
using System.Linq;
using Content.Server.CMU14.Requisitions;
using Content.Shared.CMU14.Requisitions;
using Content.Shared.CMU14.Robotics;
using Content.Shared.FixedPoint;
using Content.Shared.Storage.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Robotics;

// Crate delivery and repair-unit preparation.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private void OnCrateDelivered(ref CMURequisitionsCrateDeliveredEvent args)
    {
        // Every WEYU terminal shares the same pool, so one crate event can match several console
        // components. Track the order that was already handled instead of processing the crate twice.
        var processedOrderId = -1;
        var query = EntityQueryEnumerator<CMURoboticsOrderConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var console))
        {
            var pool = _pools.GetPool(consoleUid);
            if (pool.ActiveOrder is not { } order
                || order.Elevator != args.Elevator.Owner
                || processedOrderId == order.Id
                || !TryComp<CMURoboticsTransportCrateComponent>(args.Crate, out var crate)
                || crate.OrderId != order.Id)
                continue;

            processedOrderId = order.Id;
            MarkDeliveredCrate((consoleUid, console), order, args.Crate);
        }
    }

    private void MarkDeliveredCrate(
        Entity<CMURoboticsOrderConsoleComponent> console,
        CMURoboticsOrder order,
        EntityUid crate)
    {
        if (!HasComp<CMURoboticsTransportCrateComponent>(crate))
            return;

        var crateComp = EnsureComp<CMURoboticsTransportCrateComponent>(crate);
        crateComp.OrderId = order.Id;

        if (!TryComp<EntityStorageComponent>(crate, out var storage))
            return;

        var containedEntities = new List<EntityUid>(storage.Contents.ContainedEntities);
        foreach (var contained in containedEntities)
        {
            if (!HasComp<CMURoboticsDamagedBodyComponent>(contained))
                continue;

            var marker = EnsureComp<CMURoboticsOrderTargetComponent>(contained);
            marker.OrderId = order.Id;

            if (order.Type != CMURoboticsOrderType.Repair)
                continue;

            _damagedBody.ApplyRepairProfile(
                contained,
                order.RepairMissingLimbs,
                order.RepairBurnDamage,
                order.RepairBruteDamage);

            marker.InitialDamage = FixedPoint2.New(order.RepairBurnDamage + order.RepairBruteDamage);
            marker.InitialMissingLimbs = order.RepairMissingLimbs;
            ApplyWorkingJoeOutfit(contained, marker, order, Transform(crate).Coordinates);
        }
    }

    private void ApplyWorkingJoeOutfit(
        EntityUid unit,
        CMURoboticsOrderTargetComponent marker,
        CMURoboticsOrder order,
        EntityCoordinates spawnCoordinates)
    {
        if (!order.WorkingJoe || order.Outfits.Count == 0)
            return;

        var available = Enumerable.Range(0, order.Outfits.Count)
            .Where(index => !order.UsedOutfitIndices.Contains(index))
            .ToList();
        if (available.Count == 0)
        {
            order.UsedOutfitIndices.Clear();
            available = Enumerable.Range(0, order.Outfits.Count).ToList();
        }

        var outfitIndex = _random.Pick(available);
        order.UsedOutfitIndices.Add(outfitIndex);
        var outfit = order.Outfits[outfitIndex];

        foreach (var (slot, prototype) in outfit.Equipment)
        {
            var item = Spawn(prototype, spawnCoordinates);
            if (!_inventory.TryEquip(unit, item, slot, silent: true, force: true))
            {
                QueueDel(item);
                continue;
            }

            marker.Outfit[slot] = prototype;
        }
    }
}
