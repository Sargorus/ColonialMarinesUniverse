using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Robotics;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.ViewVariables;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// The WEYU robotics order pool. Every robotics terminal on the same faction account draws from this one
/// queue, so buying a second terminal shares both the offers and the active contract instead of cloning them.
/// </summary>
public sealed partial class CMURoboticsOrderPoolSystem : EntitySystem
{
    /// <summary>Research account shared by every WEYU robotics terminal.</summary>
    private const string WEYUResearchFaction = "corporate";

    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<string, CMURoboticsOrderPool> _pools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the shared pool for a robotics terminal.</summary>
    public CMURoboticsOrderPool GetPool(EntityUid console)
    {
        // Every WEYU terminal draws on the corporate research account. The console argument keeps
        // call sites self-documenting and leaves room for faction-owned terminals later.
        return GetPool(WEYUResearchFaction);
    }

    /// <summary>Returns the shared pool for a research faction.</summary>
    private CMURoboticsOrderPool GetPool(string faction)
    {
        if (_pools.TryGetValue(faction, out var pool))
            return pool;

        pool = new CMURoboticsOrderPool(faction);
        _pools[faction] = pool;
        return pool;
    }

    public void Reset()
    {
        _pools.Clear();
    }

    /// <summary>Seconds left before another drone build/repair order may be accepted.</summary>
    public int DroneCooldownSeconds(CMURoboticsOrderPool pool, TimeSpan orderCooldown)
    {
        return CMURoboticsOrderCooldown.RemainingSeconds(orderCooldown, pool.NextDroneOrderAllowed, _timing.CurTime);
    }

    /// <summary>Seconds left before another SMO production-network contract may be started.</summary>
    public int NetworkCooldownSeconds(CMURoboticsOrderPool pool, TimeSpan orderCooldown)
    {
        return CMURoboticsOrderCooldown.RemainingSeconds(orderCooldown, pool.NextNetworkOrderAllowed, _timing.CurTime);
    }
}

/// <summary>Round-scoped state shared by every terminal drawing on one faction's robotics account.</summary>
public sealed class CMURoboticsOrderPool(string faction)
{
    public readonly string Faction = faction;

    [ViewVariables]
    public List<CMURoboticsOrder> AvailableOrders = new();

    [ViewVariables]
    public CMURoboticsOrder? ActiveOrder;

    [ViewVariables]
    public string Status = string.Empty;

    [ViewVariables]
    public int LastPenalty;

    [ViewVariables]
    public int LastMissingElements;

    [ViewVariables]
    public TimeSpan NextDroneOrderAllowed;

    [ViewVariables]
    public TimeSpan NextNetworkOrderAllowed;
}
