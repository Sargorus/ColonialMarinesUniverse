using System;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.DroneOperator;

public sealed partial class CMUDroneEndControlActionEvent : InstantActionEvent;

public sealed partial class CMUDroneFollowActionEvent : InstantActionEvent;

public sealed partial class CMUDroneStopFollowActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class CMUDroneReviveDoAfterEvent : SimpleDoAfterEvent;
