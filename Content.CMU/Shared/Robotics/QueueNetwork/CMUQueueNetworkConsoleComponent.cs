using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Shared.CMU14.Robotics.QueueNetwork;

/// <summary>
/// A production diagnostic terminal that offers queueing-network contracts.
/// The player analyzes nodes, finds the ones without a steady state, upgrades
/// service rates within a maintenance budget, and submits the line once all
/// queues are stable.
/// </summary>
[RegisterComponent]
public sealed partial class CMUQueueNetworkConsoleComponent : Component
{
    /// <summary>Scenarios this terminal can offer.</summary>
    [DataField]
    public List<ProtoId<CMUQueueNetworkPrototype>> Scenarios = new();

    [ViewVariables]
    public ProtoId<CMUQueueNetworkPrototype>? ActiveScenario;

    [ViewVariables]
    public Dictionary<string, CMUQueueNodeRuntime> Nodes = new();

    [ViewVariables]
    public Dictionary<string, CMUQueueAnalysis> Analyses = new();

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

    [ViewVariables]
    public bool Failed;

    /// <summary>Contracts completed on this terminal, used to offer the tutorial first.</summary>
    [ViewVariables]
    public int CompletedContracts;
}

public sealed class CMUQueueNodeRuntime
{
    public double ServiceRate;
    public int Upgrades;
    public double Queue;
}

public sealed class CMUQueueAnalysis
{
    public double Lambda;
    public CMUQueueMetrics Metrics;
}

public sealed class CMUQueueHistoryPoint
{
    public int Minute;
    public Dictionary<string, double> Queues = new();
}
