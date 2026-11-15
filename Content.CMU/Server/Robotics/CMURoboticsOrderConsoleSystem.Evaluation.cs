using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Medical.Anatomy.BodyParts;
using Content.Shared.CMU14.Requisitions;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Storage.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Robotics;

// Contract evaluation against delivered units.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private void OnElevatorLoweringAttempt(
        Entity<RequisitionsElevatorComponent> ent,
        ref CMURequisitionsElevatorLoweringAttemptEvent args)
    {
        var query = EntityQueryEnumerator<CMURoboticsOrderConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var console))
        {
            var pool = _pools.GetPool(consoleUid);
            if (pool.ActiveOrder is not { } order || order.Elevator != ent.Owner)
                continue;

            var success = TryEvaluateSubmission(
                ent.Owner,
                order,
                out var missingElements,
                out var attempted,
                out var reason);
            if (!attempted)
                continue;

            var consoleEnt = (consoleUid, console);
            if (!success)
            {
                FailOrder(consoleEnt, pool, order, fine: false, reason: reason);
                continue;
            }

            var penalty = CalculatePenalty(order.Reward, missingElements);
            var paid = order.Reward - penalty;
            PayCorporateBudget(paid);
            AwardRoboticsPoints(paid, pool.Faction);
            DeleteOrderSlips(order.Id);
            pool.ActiveOrder = null;
            pool.LastPenalty = penalty;
            pool.LastMissingElements = missingElements;
            pool.Status = penalty > 0
                ? Loc.GetString(
                    "cmu-robotics-console-delivered-penalty",
                    ("target", order.TargetName),
                    ("reward", paid),
                    ("penalty", penalty),
                    ("missing", missingElements))
                : Loc.GetString(
                    "cmu-robotics-console-delivered",
                    ("target", order.TargetName),
                    ("reward", order.Reward));
            FillOrders(consoleEnt);
            UpdateUi(consoleEnt);
        }
    }

    private bool TryEvaluateSubmission(
        EntityUid elevator,
        CMURoboticsOrder order,
        out int missingElements,
        out bool attempted,
        out string reason)
    {
        missingElements = 0;
        attempted = false;
        reason = string.Empty;

        var (crates, looseSlip) = GetOrderDeliverySnapshot(elevator, order);
        var units = crates.SelectMany(crate => crate.Units).ToList();
        var hasAnyLabel = looseSlip || crates.Any(crate => crate.HasOrderLabel);
        var labeledWithUnit = crates.Any(crate => crate.HasOrderLabel && crate.Units.Count > 0);

        attempted = hasAnyLabel || units.Count > 0;
        if (!attempted)
            return false;

        if (!hasAnyLabel)
        {
            reason = Loc.GetString("cmu-robotics-console-reason-no-slip");
            return false;
        }

        if (!labeledWithUnit)
        {
            reason = Loc.GetString("cmu-robotics-console-reason-slip-not-on-crate");
            return false;
        }

        if (units.Count < order.Amount)
            missingElements += order.Amount - units.Count;

        if (order.Type == CMURoboticsOrderType.Build)
            missingElements += crates.Count(crate => crate.Units.Count > 0 && !crate.HasControlTablet);

        foreach (var unit in units.Take(order.Amount))
        {
            if (!_mobState.IsAlive(unit)
                || !TryComp<DamageableComponent>(unit, out var damageable))
            {
                reason = Loc.GetString("cmu-robotics-console-reason-broken");
                return false;
            }

            if (_damageable.GetTotalDamage((unit, damageable)) > FixedPoint2.New(5))
                missingElements++;

            missingElements += CountMissingLimbs(unit, order);

            if (order.RequiredModule is { } required && !HasRequiredModule(unit, required))
                missingElements++;

            if (TryComp<CMURoboticsOrderTargetComponent>(unit, out var marker))
                missingElements += CountMissingOutfitElements(unit, marker);
        }

        return true;
    }

    private static int CalculatePenalty(int reward, int missingElements)
    {
        if (reward <= 0 || missingElements <= 0)
            return 0;

        var penalty = (int) Math.Ceiling(reward * MissingElementPenaltyFraction * missingElements);
        return Math.Min(reward, penalty);
    }

    private (List<OrderCrateInfo> Crates, bool LooseSlip) GetOrderDeliverySnapshot(
        EntityUid elevator,
        CMURoboticsOrder order)
    {
        var crates = new List<OrderCrateInfo>();
        var looseSlip = false;

        foreach (var entity in _lookup.GetEntitiesIntersecting(elevator))
        {
            if (TryComp<CMURoboticsOrderSlipComponent>(entity, out var slip)
                && slip.OrderId == order.Id)
                looseSlip = true;

            if (!HasComp<CMURoboticsTransportCrateComponent>(entity))
                continue;

            var hasLabel = _labels.TryGetLabel<CMURoboticsOrderSlipComponent>(entity, out var label)
                && label.Value.Comp.OrderId == order.Id;

            var units = new List<EntityUid>();
            var hasControlTablet = false;
            if (TryComp<EntityStorageComponent>(entity, out var storage))
            {
                foreach (var contained in storage.Contents.ContainedEntities)
                {
                    if (HasComp<CMUDroneControlTabletComponent>(contained))
                        hasControlTablet = true;

                    if (IsOrderUnit(contained, order))
                        units.Add(contained);
                }
            }

            crates.Add(new OrderCrateInfo(entity, units, hasLabel, hasControlTablet));
        }

        return (crates, looseSlip);
    }

    private bool IsOrderUnit(EntityUid entity, CMURoboticsOrder order)
    {
        if (order.Type == CMURoboticsOrderType.Repair)
        {
            return TryComp<CMURoboticsOrderTargetComponent>(entity, out var marker)
                && marker.OrderId == order.Id;
        }

        return HasComp<CMUDroneAndroidComponent>(entity)
            && MetaData(entity).EntityPrototype?.ID == order.Target.Id;
    }

}
