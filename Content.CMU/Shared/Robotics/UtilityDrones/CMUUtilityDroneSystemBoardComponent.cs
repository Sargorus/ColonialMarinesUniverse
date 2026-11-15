using System;
using Content.Shared.Tools;
using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.Robotics.UtilityDrones;

/// <summary>
/// A drone system board installed in a dedicated slot. The basic board enables
/// directive-board programs; the combat board also permits weapon commands.
/// </summary>
[RegisterComponent]
public sealed partial class CMUUtilityDroneSystemBoardComponent : Component
{
    [DataField]
    public bool Combat;

    [DataField]
    public ProtoId<ToolQualityPrototype> InstallTool = "Screwing";

    [DataField]
    public TimeSpan InstallDelay = TimeSpan.FromSeconds(4);
}
