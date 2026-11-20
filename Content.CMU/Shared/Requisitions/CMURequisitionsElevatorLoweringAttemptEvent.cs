namespace Content.Shared.CMU14.Requisitions;

/// <summary>
/// Raised on a requisitions elevator before it starts lowering. Robotics order
/// consoles use it to validate and pay out repairs/builds on the platform.
/// </summary>
[ByRefEvent]
public record struct CMURequisitionsElevatorLoweringAttemptEvent(EntityUid Elevator, bool Cancelled = false);
