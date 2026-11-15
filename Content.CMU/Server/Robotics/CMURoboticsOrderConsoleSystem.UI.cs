using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.Robotics;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Robotics;

// Terminal UI state and cooldown readouts.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private void UpdateUi(Entity<CMURoboticsOrderConsoleComponent> ent)
    {
        var pool = _pools.GetPool(ent.Owner);
        var available = pool.AvailableOrders
            .Select(ToInfo)
            .ToList();

        var active = pool.ActiveOrder == null ? null : ToInfo(pool.ActiveOrder);
        var cooldown = GetDroneCooldownSeconds(pool, ent.Comp);
        var networkCooldown = GetNetworkCooldownSeconds(pool, ent.Comp);

        CMUQueueNetworkConsoleState? queue = null;
        if (TryComp<CMUQueueNetworkConsoleComponent>(ent.Owner, out var queueComp)
            && (ent.Comp.LastQueueRevision != queueComp.Revision
                || ent.Comp.LastQueueCooldown != networkCooldown))
        {
            queue = _queueNetwork.BuildState((ent.Owner, queueComp));
            ent.Comp.LastQueueRevision = queueComp.Revision;
            ent.Comp.LastQueueCooldown = networkCooldown;
        }

        var cartridges = ent.Comp.Cartridges
            .Select(entry => new CMURoboticsCartridgeInfo(entry.Cartridge, entry.Description, entry.Cost))
            .ToList();

        var roboticsPoints = _research.GetRoboticsPoints(pool.Faction);

        _ui.SetUiState(
            ent.Owner,
            CMURoboticsOrderConsoleUiKey.Key,
            new CMURoboticsOrderConsoleState(
                available,
                active,
                pool.Status,
                pool.LastPenalty,
                pool.LastMissingElements,
                cooldown,
                queue,
                cartridges,
                roboticsPoints,
                ent.Comp.CartridgeStatus));
    }

    private int GetDroneCooldownSeconds(CMURoboticsOrderPool pool, CMURoboticsOrderConsoleComponent comp)
    {
        return _pools.DroneCooldownSeconds(pool, comp.OrderCooldown);
    }

    private int GetNetworkCooldownSeconds(CMURoboticsOrderPool pool, CMURoboticsOrderConsoleComponent comp)
    {
        return _pools.NetworkCooldownSeconds(pool, comp.OrderCooldown);
    }

    private CMURoboticsOrderInfo ToInfo(CMURoboticsOrder order)
    {
        var seconds = order.Deadline == default
            ? 0
            : Math.Max(0, (int) Math.Ceiling((order.Deadline - _timing.CurTime).TotalSeconds));

        return new CMURoboticsOrderInfo(
            order.Id,
            order.Type,
            order.TargetNameLocId,
            order.RequiredModule,
            order.Reward,
            order.Amount,
            seconds);
    }

    private string GetPrototypeName(EntProtoId prototype)
    {
        if (Loc.TryGetString($"ent-{prototype.Id}", out var entityName))
            return entityName;

        var name = _prototypes.Index<EntityPrototype>(prototype).Name;
        return Loc.TryGetString(name, out var localized) ? localized : name;
    }

    private readonly record struct OrderCrateInfo(
        EntityUid Crate,
        List<EntityUid> Units,
        bool HasOrderLabel,
        bool HasControlTablet);
}
