using Robust.Shared.GameStates;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Short-lived reservation placed on a patient while a medibot walks over.
/// Prevents several bots from choosing the same patient before the first
/// injection lands. The post-injection cooldown uses
/// <see cref="Content.Shared.NPC.Components.NPCRecentlyInjectedComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CMUMedibotReservationComponent : Component
{
    public EntityUid Bot;

    public TimeSpan Expires;
}
