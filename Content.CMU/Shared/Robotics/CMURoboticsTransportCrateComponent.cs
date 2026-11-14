using Robust.Shared.ViewVariables;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Marks the black lockable crate used to carry one synthetic to or from the
/// corporate cargo platform. The order console stores the contract id on
/// delivered crates so the damaged unit inside can be matched to its order.
/// </summary>
[RegisterComponent]
public sealed partial class CMURoboticsTransportCrateComponent : Component
{
    [DataField, ViewVariables]
    public int OrderId;
}
