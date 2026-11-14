using System;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared.DoAfter;
using Content.Shared.Lathe.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// A lathe that only works while a WL-6 universal fuel cell is inserted and only
/// for personnel with the required research skill. The cell is inserted by hand
/// and removed with a crowbar or the right-click verb; the fabricator itself
/// does not drain it.
/// </summary>
[RegisterComponent]
public sealed partial class CMURoboticsFabricatorComponent : Component
{
    [DataField]
    public string CellContainerSlot = "cmu_robotics_fabricator_cell";

    [DataField]
    public EntProtoId<SkillDefinitionComponent> ResearchSkill = "RMCSkillResearch";

    [DataField]
    public int ResearchLevel = 3;

    [DataField]
    public TimeSpan InsertDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan RemoveDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public ProtoId<LatheRecipePackPrototype> ProductionRecipePack = "CMURoboticsFabricatorRecipes";
}

[Serializable, NetSerializable]
public sealed partial class CMURoboticsFabricatorInsertCellDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CMURoboticsFabricatorRemoveCellDoAfterEvent : SimpleDoAfterEvent;
