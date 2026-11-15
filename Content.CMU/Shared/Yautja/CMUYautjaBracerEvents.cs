using Robust.Shared.GameObjects;

namespace Content.Shared.CMU14.Yautja;

/// <summary>
/// Raised when a recovered Yautja bracer has all of its chemical seals removed and is authorized for a
/// human operator. Company missions count one bracer per event.
/// </summary>
public sealed class CMUYautjaBracerUnlockedEvent(EntityUid bracer, EntityUid user) : EntityEventArgs
{
    public readonly EntityUid Bracer = bracer;
    public readonly EntityUid User = user;
}
