using Content.Shared.Lathe.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// A physical recipe cartridge. While inserted into a robotics fabricator it
/// unlocks the recipes in <see cref="Pack"/> for that fabricator only.
/// </summary>
[RegisterComponent]
public sealed partial class CMURoboticsRecipeCartridgeComponent : Component
{
    [DataField(required: true)]
    public ProtoId<LatheRecipePackPrototype> Pack;
}
