namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Converts contract payout into robotics science points. Robotics points are a separate currency from
/// chemistry sample data: work orders and SMO contracts fund them, and the robotics terminal spends them.
/// </summary>
public static class CMURoboticsPoints
{
    /// <summary>Cash/payout per awarded robotics point.</summary>
    public const int CashPerPoint = 1000;

    /// <summary>Every completed contract is worth at least one point, however small its payout.</summary>
    public static int FromPayout(int payout)
    {
        return Math.Max(1, payout / CashPerPoint);
    }
}
