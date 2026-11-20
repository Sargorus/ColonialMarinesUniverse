using Content.Shared.CMU14.Robotics.UtilityDrones;
using Robust.Shared.Localization;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Semantic validation for compiled utility-drone programs. The parser owns
/// syntax; this pass rejects jumps and native calls that the VM could only
/// discover at runtime.
/// </summary>
public static class CMUUtilityScriptAnalyzer
{
    /// <summary>
    /// Native function arity shared by the analyzer and tests. Values are
    /// inclusive counts; the VM still re-checks the stack when it executes.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> FunctionArity =
        new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase)
        {
            ["marker"] = (1, 1),
            ["hand"] = (1, 1),
            ["hands"] = (0, 1),
            ["activehand"] = (0, 0),
            ["equipped"] = (1, 1),
            ["worn"] = (0, 1),
            ["backpack"] = (0, 1),
            ["backpackitem"] = (1, 1),
            ["backpacknames"] = (0, 1),
            ["backpackitems"] = (0, 1),
            ["item"] = (1, 2),
            ["person"] = (1, 2),
            ["nearestperson"] = (0, 1),
            ["peoplenames"] = (0, 1),
            ["inspectperson"] = (0, 1),
            ["worncontains"] = (2, 2),
            ["people"] = (0, 1),
            ["items"] = (0, 1),
            ["entities"] = (0, 1),
            ["peoplecount"] = (0, 1),
            ["personcount"] = (0, 1),
            ["itemcount"] = (0, 1),
            ["backpackcount"] = (0, 0),
            ["name"] = (1, 1),
            ["itemname"] = (1, 1),
            ["itemid"] = (1, 1),
            ["itemdisplay"] = (1, 2),
            ["handname"] = (1, 1),
            ["inhand"] = (1, 1),
            ["hasbackpackitem"] = (1, 1),
            ["distance"] = (1, 1),
            ["damage"] = (0, 1),
            ["health"] = (0, 1),
            ["maxhealth"] = (0, 1),
            ["healthpercent"] = (0, 1),
            ["alive"] = (0, 1),
            ["dead"] = (0, 1),
            ["critical"] = (0, 1),
            ["nearesthostile"] = (0, 1),
            ["hostiles"] = (0, 1),
            ["hostilecount"] = (0, 1),
            ["nearestally"] = (0, 1),
            ["ammo"] = (0, 1),
            ["maxammo"] = (0, 1),
            ["incombat"] = (0, 0),
            ["pulling"] = (0, 1),
            ["aimtarget"] = (0, 1),
            ["exists"] = (1, 1),
            ["attacker"] = (0, 1),
            ["damagetaken"] = (0, 1),
            ["heardtext"] = (0, 0),
            ["heardspeaker"] = (0, 0),
            ["backpackfits"] = (1, 1),
        };

    public static bool TryAnalyze(CMUUtilityScriptProgram program, out string error)
    {
        error = string.Empty;

        if (program.MainStart < 0 || program.MainStart > program.Instructions.Count)
        {
            error = Loc.GetString("cmu-utility-drone-script-error-main");
            return false;
        }

        if (!TryValidateProgram(program, program, out error))
            return false;

        foreach (var handler in program.EventHandlers)
        {
            if (!TryValidateProgram(handler.Program, program, out error))
                return false;
        }

        return true;
    }

    private static bool TryValidateProgram(
        CMUUtilityScriptProgram program,
        CMUUtilityScriptProgram mainProgram,
        out string error)
    {
        error = string.Empty;

        foreach (var instruction in program.Instructions)
        {
            switch (instruction.Op)
            {
                case CMUUtilityScriptOp.Goto:
                {
                    var target = instruction.Text;
                    var targetProgram = program;
                    if (target.StartsWith("main.", StringComparison.OrdinalIgnoreCase))
                    {
                        target = target["main.".Length..];
                        targetProgram = mainProgram;
                    }

                    if (targetProgram.Labels.ContainsKey(target))
                        break;

                    error = Loc.GetString(
                        "cmu-utility-drone-script-error-label",
                        ("label", instruction.Text));
                    return false;
                }

                case CMUUtilityScriptOp.Call:
                {
                    if (!FunctionArity.TryGetValue(instruction.Text, out var arity))
                    {
                        error = Loc.GetString(
                            "cmu-utility-drone-script-error-unknown-function",
                            ("name", instruction.Text),
                            ("line", instruction.Line));
                        return false;
                    }

                    if (instruction.ArgCount < arity.Min || instruction.ArgCount > arity.Max)
                    {
                        var expected = arity.Min == arity.Max
                            ? arity.Min.ToString()
                            : $"{arity.Min}-{arity.Max}";
                        error = Loc.GetString(
                            "cmu-utility-drone-script-error-function-arity",
                            ("name", instruction.Text),
                            ("expected", expected),
                            ("count", instruction.ArgCount),
                            ("line", instruction.Line));
                        return false;
                    }

                    break;
                }
            }
        }

        return true;
    }
}
