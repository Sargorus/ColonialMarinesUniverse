using System.Collections.Generic;
using Content.Shared.Body.Part;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Spawns an android in a repair-order state: a random number of limbs are
/// missing and the frame has randomized brute and burn damage. The roboticist
/// must attach matching limbs and repair the damage before delivery.
/// </summary>
[RegisterComponent]
public sealed partial class CMURoboticsDamagedBodyComponent : Component
{
    /// <summary>
    /// Whether this unit should roll its own damage when it spawns. Order
    /// units use a stable variant and receive the pre-rolled profile when the
    /// console delivers their crate.
    /// </summary>
    [DataField]
    public bool RandomizeOnMapInit = true;

    [DataField]
    public int MinSeveredLimbs = 1;

    [DataField]
    public int MaxSeveredLimbs = 3;

    [DataField]
    public List<BodyPartType> SeverableParts = new()
    {
        BodyPartType.Arm,
        BodyPartType.Leg,
    };

    [DataField]
    public int MinBurnDamage = 40;

    [DataField]
    public int MaxBurnDamage = 90;

    [DataField]
    public int MinBruteDamage = 30;

    [DataField]
    public int MaxBruteDamage = 70;

    [DataField]
    public ProtoId<DamageTypePrototype> BurnDamageType = "Heat";

    [DataField]
    public ProtoId<DamageTypePrototype> BruteDamageType = "Blunt";
}
