using Robust.Shared.GameObjects;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Marks an entity whose hands exist for interactions such as CPR but must never hold items.
/// </summary>
[RegisterComponent]
public sealed partial class CMUHandsItemBlockComponent : Component;
