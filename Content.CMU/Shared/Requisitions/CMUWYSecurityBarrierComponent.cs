using Content.Shared.Access;
using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.Requisitions;

/// <summary>
/// Marks the corporate security barrier. A server system replaces the inherited
/// marine-barrier access, fixture layers and destruction threshold at map init,
/// because list-valued prototype inheritance appends parent entries rather than
/// replacing them.
/// </summary>
[RegisterComponent]
public sealed partial class CMUWYSecurityBarrierComponent : Component
{
    /// <summary>
    /// Access level required to lock or unlock the deployed barrier.
    /// </summary>
    [DataField]
    public ProtoId<AccessLevelPrototype> Access = "CMU14AccessWYSecurityBarrier";

    /// <summary>
    /// Destruction threshold in damage points.
    /// </summary>
    [DataField]
    public int HitPoints = 2250;
}
