using System.Collections.Generic;
using Robust.Shared.ViewVariables;

namespace Content.Shared.CMU14.Robotics.QueueNetwork;

/// <summary>
/// A production diagnostic terminal that generates queueing-network
/// contracts. The player analyzes nodes, finds the ones without a steady
/// state, upgrades service rates or diverts intake, and submits the line once
/// every queue is stable.
/// </summary>
[RegisterComponent]
public sealed partial class CMUQueueNetworkConsoleComponent : Component
{
    /// <summary>The generated network for the active contract.</summary>
    [ViewVariables]
    public CMUQueueNetworkDefinition? Network;

    [ViewVariables]
    public Dictionary<string, CMUQueueNodeRuntime> Nodes = new();

    [ViewVariables]
    public Dictionary<string, CMUQueueAnalysis> Analyses = new();

    /// <summary>Active diversion fraction per bypass id.</summary>
    [ViewVariables]
    public Dictionary<string, double> BypassFractions = new();

    [ViewVariables]
    public List<CMUQueueHistoryPoint> History = new();

    [ViewVariables]
    public int Budget;

    [ViewVariables]
    public int InitialBudget;

    [ViewVariables]
    public int TimeLeft;

    [ViewVariables]
    public int InitialTime;

    [ViewVariables]
    public int Reward;

    /// <summary>Factory clock in minutes, advanced by analyses and projections.</summary>
    [ViewVariables]
    public int Elapsed;

    [ViewVariables]
    public string Status = string.Empty;

    [ViewVariables]
    public CMUQueueStatusKind StatusKind;

    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public bool Completed;

    /// <summary>Contracts completed on this terminal, used to offer the tutorial first.</summary>
    [ViewVariables]
    public int CompletedContracts;

    /// <summary>Seed of the last generated network, kept for diagnostics.</summary>
    [ViewVariables]
    public int LastSeed;

    /// <summary>Test and admin override for the topology.</summary>
    [ViewVariables]
    public CMUQueueTopologyKind? ForcedTopology;

    /// <summary>Test and admin override for the generator seed.</summary>
    [ViewVariables]
    public int? ForcedSeed;
}

public sealed class CMUQueueNodeRuntime
{
    public double ServiceRate;
    public int Invested;
    public double Queue;
}

public sealed class CMUQueueAnalysis
{
    public double Lambda;
    public CMUQueueMetrics Metrics;
}

public sealed class CMUQueueHistoryPoint
{
    public float Minute;
    public Dictionary<string, double> Queues = new();
}
