using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.Robotics.QueueNetwork;

/// <summary>
/// A data-driven M/M/1 queueing network used by the production diagnostic
/// terminal. Nodes have a single server, an unlimited queue and FIFO service.
/// External arrivals enter at fixed rates, and every route sends a fraction of
/// a node's output to another node or out of the network.
/// </summary>
[Prototype("cmuQueueNetwork")]
public sealed partial class CMUQueueNetworkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Localization key for the network name.</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>Localization key for the contract description.</summary>
    [DataField]
    public string Description = string.Empty;

    /// <summary>Maintenance budget available for service upgrades.</summary>
    [DataField]
    public int Budget = 800;

    /// <summary>Money paid into the colony budget on a successful contract.</summary>
    [DataField]
    public int Reward = 1200;

    /// <summary>Diagnostic window in simulated minutes.</summary>
    [DataField]
    public int DiagnosticMinutes = 15;

    /// <summary>Minutes consumed by analyzing one node.</summary>
    [DataField]
    public int AnalysisCost = 1;

    /// <summary>Minutes consumed by one queue projection run.</summary>
    [DataField]
    public int ProjectionCost = 3;

    /// <summary>How many minutes one projection run covers.</summary>
    [DataField]
    public int ProjectionMinutes = 3;

    [DataField]
    public List<CMUQueueNodePrototype> Nodes = new();

    [DataField]
    public List<CMUQueueSourcePrototype> Sources = new();

    [DataField]
    public List<CMUQueueRoutePrototype> Routes = new();
}

[DataDefinition]
public sealed partial class CMUQueueNodePrototype
{
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>Localization key for the node name.</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>Base service rate in items per minute.</summary>
    [DataField]
    public double ServiceRate = 10;

    /// <summary>Multiplier applied once per upgrade.</summary>
    [DataField]
    public double UpgradeMultiplier = 1.5;

    /// <summary>Cost of one upgrade in the maintenance budget.</summary>
    [DataField]
    public int UpgradeCost = 250;

    /// <summary>Maximum number of upgrades that can be installed.</summary>
    [DataField]
    public int MaxUpgrades = 1;

    /// <summary>Queue length at the start of a contract.</summary>
    [DataField]
    public double InitialQueue;

    /// <summary>Column in the diagnostic schematic.</summary>
    [DataField]
    public int Column;

    /// <summary>Row in the diagnostic schematic.</summary>
    [DataField]
    public int Row;
}

[DataDefinition]
public sealed partial class CMUQueueSourcePrototype
{
    [DataField(required: true)]
    public string Node = string.Empty;

    /// <summary>External arrival rate in items per minute.</summary>
    [DataField]
    public double Rate = 10;
}

[DataDefinition]
public sealed partial class CMUQueueRoutePrototype
{
    [DataField(required: true)]
    public string From = string.Empty;

    /// <summary>Destination node id, or "sink" to leave the network.</summary>
    [DataField(required: true)]
    public string To = string.Empty;

    /// <summary>Fraction of the source node's output sent along this route.</summary>
    [DataField]
    public double Probability = 1;
}
