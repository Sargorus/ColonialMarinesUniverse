using Robust.Shared.GameObjects;

namespace Content.Shared.Cuffs;

/// <summary>
/// Raised after a set of restraints has been successfully removed. Company missions use it to count
/// player-caused removals of Yautja restraints without subscribing to the cuff do-after directly
/// (the event bus only permits one directed subscription per component/event).
/// </summary>
public sealed class CuffRemovedEvent(EntityUid target, EntityUid? user, EntityUid cuff) : EntityEventArgs
{
    public readonly EntityUid Target = target;
    public readonly EntityUid? User = user;
    public readonly EntityUid Cuff = cuff;
}
