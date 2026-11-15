using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Turrets;

/// <summary>
/// Raised when the timed action to toggle a deployable turret finishes.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class DeployableTurretToggleDoAfterEvent : DoAfterEvent
{
    /// <summary>
    /// The state the turret should be in after the action completes.
    /// Captured when the action starts so that any unrelated state changes
    /// during the action cannot flip the result.
    /// </summary>
    [DataField]
    public bool Enabled;

    public override DoAfterEvent Clone() => new DeployableTurretToggleDoAfterEvent { Enabled = Enabled };
}
