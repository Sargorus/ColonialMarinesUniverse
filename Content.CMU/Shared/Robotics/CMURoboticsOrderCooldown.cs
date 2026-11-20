using System;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Shared countdown math for the robotics terminal's independent drone and
/// production-network queues.
/// </summary>
public static class CMURoboticsOrderCooldown
{
    public static int RemainingSeconds(TimeSpan cooldown, TimeSpan nextAllowed, TimeSpan now)
    {
        if (cooldown <= TimeSpan.Zero || nextAllowed <= now)
            return 0;

        return (int)Math.Ceiling((nextAllowed - now).TotalSeconds);
    }
}
