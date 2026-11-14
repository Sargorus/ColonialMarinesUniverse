using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// A corporate cargo terminal that offers robotics work orders. Build orders
/// ask for a specific android model; repair orders queue synthetic transport
/// crates with damaged units for the corporate cargo elevator. Accepting an
/// order prints a work-order slip; finished units go into those crates and the
/// slip is attached to the crate before the platform is lowered.
/// </summary>
[RegisterComponent]
public sealed partial class CMURoboticsOrderConsoleComponent : Component
{
    [DataField]
    public int MaxAvailableOrders = 3;

    [DataField]
    public TimeSpan OrderDuration = TimeSpan.FromMinutes(15);

    [DataField]
    public EntProtoId SlipPrototype = "CMURoboticsOrderSlip";

    [DataField]
    public List<CMURoboticsOrderTarget> BuildTargets = new();

    [DataField]
    public List<CMURoboticsOrderTarget> RepairTargets = new();

    [ViewVariables]
    public List<CMURoboticsOrder> AvailableOrders = new();

    [ViewVariables]
    public CMURoboticsOrder? ActiveOrder;

    [ViewVariables]
    public string Status = string.Empty;

    /// <summary>
    /// Missing-element penalty applied to the last delivered order.
    /// </summary>
    [ViewVariables]
    public int LastPenalty;

    /// <summary>
    /// Number of missing elements that produced <see cref="LastPenalty"/>.
    /// </summary>
    [ViewVariables]
    public int LastMissingElements;

    [ViewVariables]
    public TimeSpan NextUiUpdate;
}

[DataDefinition]
public sealed partial class CMURoboticsOrderTarget
{
    [DataField]
    public EntProtoId Prototype = string.Empty;

    /// <summary>
    /// Optional localized display name for the order list and paper. Falls
    /// back to the entity prototype name when empty.
    /// </summary>
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public int Reward = 300;

    /// <summary>
    /// Optional android modules the customer wants installed. One is picked at
    /// random when the order is generated.
    /// </summary>
    [DataField]
    public List<EntProtoId> RequiredModules = new();

    /// <summary>
    /// Prototype spawned inside a repair crate. Required for repair targets; build
    /// targets leave it empty.
    /// </summary>
    [DataField]
    public EntProtoId? RepairPrototype;

    /// <summary>
    /// Whether the target uses the Working Joe limb family instead of drone limbs.
    /// </summary>
    [DataField]
    public bool WorkingJoe;

    /// <summary>
    /// Outfits a Working Joe repair target can arrive in. One is picked per unit.
    /// </summary>
    [DataField]
    public List<CMURoboticsOrderOutfit> Outfits = new();
}

[DataDefinition]
public sealed partial class CMURoboticsOrderOutfit
{
    /// <summary>
    /// Inventory slots and the clothing prototypes equipped in them.
    /// </summary>
    [DataField]
    public Dictionary<string, EntProtoId> Equipment = new();
}

public sealed class CMURoboticsOrder
{
    public int Id;
    public CMURoboticsOrderType Type;
    public EntProtoId Target = string.Empty;
    public EntProtoId? RequiredModule;
    public int Reward;
    public int Amount = 1;
    public string TargetName = string.Empty;
    public string TargetNameLocId = string.Empty;
    public EntProtoId RepairTarget = string.Empty;
    public bool WorkingJoe;
    public List<CMURoboticsOrderOutfit> Outfits = new();
    public List<int> UsedOutfitIndices = new();

    /// <summary>Pre-rolled repair profile, applied to each unit when its crate arrives.</summary>
    public int RepairBurnDamage;
    public int RepairBruteDamage;
    public int RepairMissingLimbs;

    public TimeSpan Deadline;
    public EntityUid? Elevator;
}
