using System.Collections.Generic;

namespace Content.Shared.CMU14.Robotics.QueueNetwork;

/// <summary>
/// A generated queueing network. Unlike a prototype, this is mutable runtime
/// state owned by one diagnostic terminal; the generator creates a fresh
/// instance for every contract.
/// </summary>
public sealed class CMUQueueNetworkDefinition
{
    /// <summary>Localization key for the network name.</summary>
    public string Name = string.Empty;

    /// <summary>Localization key for the contract description.</summary>
    public string Description = string.Empty;

    public CMUQueueTopologyKind Topology;

    /// <summary>Seed used by the generator, kept for diagnostics and tests.</summary>
    public int Seed;

    public int Budget = 800;
    public int Reward = 1200;
    public int DiagnosticMinutes = 15;
    public int AnalysisCost = 1;
    public int ProjectionCost = 3;
    public int ProjectionMinutes = 3;

    public List<CMUQueueNodeDefinition> Nodes = new();
    public List<CMUQueueSourceDefinition> Sources = new();
    public List<CMUQueueRouteDefinition> Routes = new();
    public List<CMUQueueBypassDefinition> Bypasses = new();
}

public sealed class CMUQueueNodeDefinition
{
    public string Id = string.Empty;

    /// <summary>Localization key for the node role.</summary>
    public string Name = string.Empty;

    /// <summary>Optional variant suffix such as "A" or "B".</summary>
    public string Suffix = string.Empty;

    public double ServiceRate = 10;
    public double UpgradeMultiplier = 1.5;
    public int UpgradeCost = 250;
    public int MaxUpgrades = 1;
    public double InitialQueue;
}

public sealed class CMUQueueSourceDefinition
{
    public string Node = string.Empty;
    public double Rate = 10;
}

public sealed class CMUQueueRouteDefinition
{
    public string From = string.Empty;

    /// <summary>Destination node id, or "sink" to leave the network.</summary>
    public string To = string.Empty;

    public double Probability = 1;
}

/// <summary>
/// A player-controlled intake diversion. A fraction of the external flow
/// entering <see cref="From"/> is moved directly to <see cref="To"/>, which
/// lowers the source node's offered load at the cost of loading the target.
/// </summary>
public sealed class CMUQueueBypassDefinition
{
    public string Id = string.Empty;
    public string From = string.Empty;
    public string To = string.Empty;
    public double MaxFraction = 0.3;
    public double StepFraction = 0.1;
    public int CostPerStep = 100;
}

public enum CMUQueueTopologyKind : byte
{
    Line,
    Branch,
    Rework,
    SplitMerge,
}
