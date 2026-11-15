namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Classifies compiled instructions that require a combat-rated drone program
/// system board. Kept separate from the compiler so the compiler stays focused
/// on parsing and bytecode emission.
/// </summary>
internal static class CMUUtilityScriptCombat
{
    public static bool Detect(CMUUtilityScriptProgram program)
    {
        foreach (var instruction in program.Instructions)
        {
            if (IsCombatOp(instruction.Op))
                return true;
        }

        foreach (var handler in program.EventHandlers)
        {
            if (Detect(handler.Program))
                return true;
        }

        return false;
    }

    private static bool IsCombatOp(CMUUtilityScriptOp op)
    {
        return op is CMUUtilityScriptOp.Wield
            or CMUUtilityScriptOp.Unwield
            or CMUUtilityScriptOp.AimEntity
            or CMUUtilityScriptOp.AimCoordinates
            or CMUUtilityScriptOp.AimClear
            or CMUUtilityScriptOp.Shoot
            or CMUUtilityScriptOp.ShootAt
            or CMUUtilityScriptOp.Reload
            or CMUUtilityScriptOp.HoldFire
            or CMUUtilityScriptOp.FreeFire
            or CMUUtilityScriptOp.Fight
            or CMUUtilityScriptOp.Disengage;
    }
}
