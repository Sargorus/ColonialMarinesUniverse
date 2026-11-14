using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Server-only execution state for a utility drone. Keeping the script VM out
/// of the shared agent component prevents server runtime data from leaking
/// into shared/networked content contracts.
/// </summary>
[RegisterComponent]
public sealed partial class CMUUtilityDroneRuntimeComponent : Component
{
    public CMUUtilityScriptExecution? ScriptExecution;
}
