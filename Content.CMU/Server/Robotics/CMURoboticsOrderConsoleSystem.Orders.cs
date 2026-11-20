using System;
using System.Linq;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Database;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Robotics;

// Order acceptance, cancellation, failures and payouts.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private void OnAcceptOrder(Entity<CMURoboticsOrderConsoleComponent> ent, ref CMURoboticsAcceptOrderMessage args)
    {
        var pool = _pools.GetPool(ent.Owner);
        if (pool.ActiveOrder != null)
        {
            pool.Status = Loc.GetString("cmu-robotics-console-active-exists");
            UpdateUi(ent);
            return;
        }

        var cooldown = GetDroneCooldownSeconds(pool, ent.Comp);
        if (cooldown > 0)
        {
            pool.Status = Loc.GetString("cmu-robotics-console-cooldown-message", ("seconds", cooldown));
            UpdateUi(ent);
            return;
        }

        var requestedId = args.Id;
        var order = pool.AvailableOrders.FirstOrDefault(o => o.Id == requestedId);
        if (order == null)
            return;

        if (FindCorporateElevator(ent.Owner) is not { } elevator)
        {
            pool.Status = Loc.GetString("cmu-robotics-console-no-platform");
            UpdateUi(ent);
            return;
        }

        var entries = BuildOrderEntries(order);
        var freeSlots = _requisitions.GetElevatorRemainingOrderSlots(elevator);
        if (!_requisitions.TryAddElevatorOrders(elevator, entries))
        {
            pool.Status = Loc.GetString(
                "cmu-robotics-console-no-queue-space",
                ("needed", entries.Count),
                ("free", freeSlots));
            UpdateUi(ent);
            return;
        }

        order.Elevator = elevator.Owner;
        order.Deadline = _timing.CurTime + ent.Comp.OrderDuration;

        pool.AvailableOrders.Remove(order);
        pool.ActiveOrder = order;
        pool.NextDroneOrderAllowed = _timing.CurTime + ent.Comp.OrderCooldown;
        pool.LastPenalty = 0;
        pool.LastMissingElements = 0;
        pool.Status = Loc.GetString(
            order.Type == CMURoboticsOrderType.Repair
                ? "cmu-robotics-console-accepted-repair"
                : "cmu-robotics-console-accepted-build",
            ("target", order.TargetName),
            ("amount", order.Amount),
            ("crates", entries.Count),
            ("reward", order.Reward));
        ent.Comp.NextUiUpdate = _timing.CurTime;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor)} accepted robotics contract {order.Id} " +
            $"({order.Type}) for {order.Reward} at {ToPrettyString(ent.Owner)}");
        PrintSlip(ent, order, args.Actor);
        UpdateUi(ent);
    }

    private void OnCancelOrder(Entity<CMURoboticsOrderConsoleComponent> ent, ref CMURoboticsCancelOrderMessage args)
    {
        var pool = _pools.GetPool(ent.Owner);
        if (pool.ActiveOrder is not { } order)
            return;

        // Cancelling a contract must not delete customer units or transport
        // crates. Only the paperwork is removed.
        DeleteOrderSlips(order.Id);

        pool.ActiveOrder = null;
        pool.LastPenalty = 0;
        pool.LastMissingElements = 0;
        pool.Status = Loc.GetString("cmu-robotics-console-cancelled");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.Actor)} cancelled robotics contract {order.Id} at {ToPrettyString(ent.Owner)}");
        FillOrders(ent);
        UpdateUi(ent);
    }

    private void OnRefreshOrders(Entity<CMURoboticsOrderConsoleComponent> ent, ref CMURoboticsRefreshOrdersMessage args)
    {
        var pool = _pools.GetPool(ent.Owner);
        if (pool.ActiveOrder != null)
        {
            pool.Status = Loc.GetString("cmu-robotics-console-refresh-active");
            UpdateUi(ent);
            return;
        }

        pool.AvailableOrders.Clear();
        FillOrders(ent);
        pool.Status = Loc.GetString("cmu-robotics-console-refreshed");
        UpdateUi(ent);
    }

    private void FailOrder(
        Entity<CMURoboticsOrderConsoleComponent> console,
        CMURoboticsOrderPool pool,
        CMURoboticsOrder order,
        bool fine,
        string? reason)
    {
        var fineAmount = 0;
        if (fine)
        {
            fineAmount = (int) Math.Ceiling(order.Reward * RepairFailureFineMultiplier);
            _corporateConsole.AddToCorporateBudget(-fineAmount);
        }

        // A failed or expired contract does not delete its customer units.
        DeleteOrderSlips(order.Id);
        pool.ActiveOrder = null;
        pool.LastPenalty = 0;
        pool.LastMissingElements = 0;
        pool.Status = reason == null
            ? Loc.GetString("cmu-robotics-console-failed-timeout", ("fine", fineAmount))
            : Loc.GetString("cmu-robotics-console-rejected", ("reason", reason));
        _adminLog.Add(LogType.Action, fine ? LogImpact.High : LogImpact.Medium,
            $"Robotics contract {order.Id} failed at {ToPrettyString(console.Owner)}: " +
            $"{reason ?? "deadline expired"}; fine {fineAmount}");
        FillOrders(console);
        UpdateUi(console);
    }

    private void DeleteOrderSlips(int orderId)
    {
        var query = EntityQueryEnumerator<CMURoboticsOrderSlipComponent>();
        while (query.MoveNext(out var uid, out var slip))
        {
            if (slip.OrderId == orderId && !TerminatingOrDeleted(uid))
                QueueDel(uid);
        }
    }

    private void PayCorporateBudget(int amount)
    {
        _corporateConsole.AddToCorporateBudget(amount);
    }

    private void AwardRoboticsPoints(int payout, string faction)
    {
        var points = CMURoboticsPoints.FromPayout(payout);
        _research.UpdateRoboticsPoints(_research.GetRoboticsPoints(faction) + points, faction);
    }
}
