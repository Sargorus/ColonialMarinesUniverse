using System;
using System.Collections.Generic;
using Content.Server.CMU14.Requisitions;
using Content.Shared._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared.CMU14.Robotics;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Robotics;

// Order generation and terminal configuration lookups.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private List<RequisitionsEntry> BuildOrderEntries(CMURoboticsOrder order)
    {
        var count = order.Type == CMURoboticsOrderType.Repair
            ? Math.Max(1, order.Amount)
            : 1;

        var entries = new List<RequisitionsEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = new RequisitionsEntry
            {
                Crate = TransportCrate,
                CMUOrderId = order.Id,
            };

            if (order.Type == CMURoboticsOrderType.Repair)
                entry.Entities.Add(order.RepairTarget);

            entries.Add(entry);
        }

        return entries;
    }

    private static EntProtoId GetRepairPrototype(CMURoboticsOrderTarget target)
    {
        return target.RepairPrototype ?? target.Prototype;
    }

    private void FillOrders(Entity<CMURoboticsOrderConsoleComponent> ent)
    {
        var pool = _pools.GetPool(ent.Owner);
        while (pool.AvailableOrders.Count < ent.Comp.MaxAvailableOrders)
        {
            var build = pool.AvailableOrders.Count == 0 || _random.Prob(0.5f);
            var targets = build ? ent.Comp.BuildTargets : ent.Comp.RepairTargets;

            if (targets.Count == 0)
            {
                build = true;
                targets = ent.Comp.BuildTargets;
            }

            if (targets.Count == 0)
                break;

            var target = _random.Pick(targets);
            EntProtoId? requiredModule = null;
            if (target.RequiredModules.Count > 0 && (!build || _random.Prob(0.5f)))
                requiredModule = _random.Pick(target.RequiredModules);

            var amount = build ? 1 : 3;
            var reward = target.Reward + (requiredModule != null ? 150 : 0) + _random.Next(0, 101);
            var repairBurn = 0;
            var repairBrute = 0;
            var repairMissing = 0;

            if (!build
                && _prototypes.TryIndex<EntityPrototype>(GetRepairPrototype(target), out var repairPrototype)
                && repairPrototype.TryComp<CMURoboticsDamagedBodyComponent>(out var damaged, _compFactory))
            {
                repairMissing = RollRange(damaged.MinSeveredLimbs, damaged.MaxSeveredLimbs);
                repairBurn = RollRange(damaged.MinBurnDamage, damaged.MaxBurnDamage);
                repairBrute = RollRange(damaged.MinBruteDamage, damaged.MaxBruteDamage);

                var unitBonus = (int) Math.Ceiling((repairBurn + repairBrute) * (double) RepairRewardPerDamage) +
                                repairMissing * RepairRewardPerMissingLimb;
                reward += unitBonus * amount;
            }

            var order = new CMURoboticsOrder
            {
                Id = _nextOrderId++,
                Type = build ? CMURoboticsOrderType.Build : CMURoboticsOrderType.Repair,
                Target = target.Prototype,
                RepairTarget = build ? default : GetRepairPrototype(target),
                WorkingJoe = target.WorkingJoe,
                Outfits = target.Outfits,
                RequiredModule = requiredModule,
                Reward = reward,
                Amount = amount,
                TargetName = string.IsNullOrEmpty(target.Name)
                    ? GetPrototypeName(target.Prototype)
                    : Loc.GetString(target.Name),
                TargetNameLocId = string.IsNullOrEmpty(target.Name)
                    ? $"ent-{target.Prototype.Id}"
                    : target.Name,
                RepairBurnDamage = repairBurn,
                RepairBruteDamage = repairBrute,
                RepairMissingLimbs = repairMissing,
            };

            pool.AvailableOrders.Add(order);
        }
    }

    private int RollRange(int min, int max)
    {
        var low = Math.Min(min, max);
        var high = Math.Max(min, max);
        return _random.Next(low, high + 1);
    }

    private Entity<RequisitionsElevatorComponent>? FindCorporateElevator(EntityUid console)
    {
        var map = _transform.GetMapId(console);
        var origin = _transform.GetWorldPosition(console);
        Entity<RequisitionsElevatorComponent>? closest = null;
        var closestDistance = float.MaxValue;

        var query = EntityQueryEnumerator<RequisitionsElevatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var elevator, out var xform))
        {
            if (xform.MapID != map
                || !string.Equals(elevator.Faction, "corporate", StringComparison.OrdinalIgnoreCase))
                continue;

            var distance = (origin - _transform.GetWorldPosition(uid)).LengthSquared();
            if (distance >= closestDistance)
                continue;

            closest = (uid, elevator);
            closestDistance = distance;
        }

        return closest;
    }
}
