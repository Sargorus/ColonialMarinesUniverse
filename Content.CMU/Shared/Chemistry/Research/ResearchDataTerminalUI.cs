using Content.Shared.CMU14.Chemistry.Reagents;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Chemistry.Research;

[Serializable, NetSerializable]
public enum ResearchDataTerminalUI
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ResearchDataTerminalBuiState(
    List<GeneratedReagentData> ids,
    Dictionary<int, (string, string, TimeSpan, bool, GeneratedReagentData, bool, bool)> data,
    TimeSpan nextUpdate,
    TimeSpan lastTime,
    int credits,
    int sampleData,
    int clearance,
    int upgradecost,
    TimeSpan? xLockedUntil,
    bool picked,
    List<ResearchContractEntry>? contracts = null,
    bool locked = false,
    string? lockedReason = null,
    List<ResearchGuaranteeEntry>? guarantees = null,
    string? guaranteedProperty = null,
    int guaranteedPropertyCost = 0) : BoundUserInterfaceState
{
    public readonly List<GeneratedReagentData> IDs = ids;
    public readonly Dictionary<int, (string, string, TimeSpan, bool, GeneratedReagentData, bool, bool)> Data = data;
    public readonly TimeSpan NextUpdate = nextUpdate;
    public readonly TimeSpan LastTime = lastTime;
    public readonly int Credits = credits;
    public readonly int SampleData = sampleData;
    public readonly int Clearance = clearance;
    public readonly int UpgradeCost = upgradecost;
    public readonly TimeSpan? XLockedUntil = xLockedUntil;
    public readonly bool Picked = picked;
    /// <summary>ARCH-03: every contract the faction has picked, formatted like the contract paper.</summary>
    public readonly List<ResearchContractEntry> Contracts = contracts ?? new();
    /// <summary>FLOW-24: the faction may not research right now (GOVFOR during Insurgency).</summary>
    public readonly bool Locked = locked;
    /// <summary>FLOW-24: the message shown in place of the contract offers while <see cref="Locked"/>.</summary>
    public readonly string? LockedReason = lockedReason;
    /// <summary>Properties that can be forced into the next contract offer, with their point costs.</summary>
    public readonly List<ResearchGuaranteeEntry> Guarantees = guarantees ?? new();
    /// <summary>The property currently guaranteed for the next offer, if any.</summary>
    public readonly string? GuaranteedProperty = guaranteedProperty;
    /// <summary>The point cost already paid for <see cref="GuaranteedProperty"/>.</summary>
    public readonly int GuaranteedPropertyCost = guaranteedPropertyCost;
}

/// <summary>One property that can be guaranteed as the first property of the next contract.</summary>
[Serializable, NetSerializable]
public readonly record struct ResearchGuaranteeEntry(string PropertyId, string Name, int Cost);

/// <summary>One entry of the terminal's Contracts tab: the chemical's name and its server-formatted contract text.</summary>
[Serializable, NetSerializable]
public readonly record struct ResearchContractEntry(string Name, string Text);

[Serializable, NetSerializable]
public sealed class ResearchDataTerminalPickChemBuiMsg(string pick) : BoundUserInterfaceMessage
{
    public readonly string Pick = pick;
}

[Serializable, NetSerializable]
public sealed class ResearchDataTerminalAttemptUpgradeBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResearchDataTerminalPrintLastBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResearchDataTerminalPrintChemBuiMsg(int idx) : BoundUserInterfaceMessage
{
    public readonly int Index = idx;
}

[Serializable, NetSerializable]
public sealed class CMUResearchReduceCooldownBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResearchDataTerminalBuyGuaranteeBuiMsg(string propertyId) : BoundUserInterfaceMessage
{
    public readonly string PropertyId = propertyId;
}
