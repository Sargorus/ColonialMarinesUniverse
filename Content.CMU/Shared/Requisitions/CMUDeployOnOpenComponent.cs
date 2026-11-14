using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Shared.CMU14.Requisitions;

/// <summary>
/// Spawns the listed prototypes next to the crate when it is opened, then
/// removes the crate. Used for ASRS products whose payload is a structure or
/// machine that cannot be carried as an ordinary storage item.
/// </summary>
[RegisterComponent]
public sealed partial class CMUDeployOnOpenComponent : Component
{
    [DataField]
    public List<EntProtoId> Spawn = new();

    [ViewVariables]
    public bool Deployed;
}
