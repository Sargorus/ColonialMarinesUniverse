using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Marks a damaged unit spawned by a robotics repair order so the console can
/// verify that this exact unit was repaired and returned. The initial damage
/// and missing-limb count are recorded when the crate arrives.
/// </summary>
[RegisterComponent]
public sealed partial class CMURoboticsOrderTargetComponent : Component
{
    [DataField]
    public int OrderId;

    [DataField]
    public FixedPoint2 InitialDamage;

    [DataField]
    public int InitialMissingLimbs;

    /// <summary>
    /// Clothing elements the unit arrived with, keyed by inventory slot.
    /// </summary>
    [DataField]
    public Dictionary<string, EntProtoId> Outfit = new();
}
