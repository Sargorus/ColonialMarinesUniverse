using Robust.Shared.Localization;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Pure stack operations shared by the server VM and unit tests. Actions and
/// world-aware natives stay in the entity system; arithmetic, comparisons and
/// truthiness are deterministic and need no entity context.
/// </summary>
public static class CMUUtilityScriptVm
{
    public static void ExecuteBinary(CMUUtilityScriptFrame frame, string op)
    {
        if (!frame.TryPop(out var right) || !frame.TryPop(out var left))
            return;

        switch (op)
        {
            case "+":
                if (left.Type == CMUUtilityScriptValueType.String ||
                    right.Type == CMUUtilityScriptValueType.String)
                {
                    frame.Push(CMUUtilityScriptValue.FromString(left.AsText + right.AsText));
                }
                else
                {
                    frame.Push(CMUUtilityScriptValue.FromNumber(left.Number + right.Number));
                }

                break;
            case "-":
                frame.Push(CMUUtilityScriptValue.FromNumber(left.Number - right.Number));
                break;
            case "*":
                frame.Push(CMUUtilityScriptValue.FromNumber(left.Number * right.Number));
                break;
            case "/":
                if (right.Number == 0)
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-division");
                    return;
                }

                frame.Push(CMUUtilityScriptValue.FromNumber(left.Number / right.Number));
                break;
            case "%":
                if (right.Number == 0)
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-division");
                    return;
                }

                frame.Push(CMUUtilityScriptValue.FromNumber(left.Number % right.Number));
                break;
            case "==":
                frame.Push(CMUUtilityScriptValue.FromBool(EqualsValue(left, right)));
                break;
            case "!=":
                frame.Push(CMUUtilityScriptValue.FromBool(!EqualsValue(left, right)));
                break;
            case "<":
                frame.Push(CMUUtilityScriptValue.FromBool(CompareValue(left, right) < 0));
                break;
            case "<=":
                frame.Push(CMUUtilityScriptValue.FromBool(CompareValue(left, right) <= 0));
                break;
            case ">":
                frame.Push(CMUUtilityScriptValue.FromBool(CompareValue(left, right) > 0));
                break;
            case ">=":
                frame.Push(CMUUtilityScriptValue.FromBool(CompareValue(left, right) >= 0));
                break;
            case "&&":
                frame.Push(CMUUtilityScriptValue.FromBool(left.IsTruthy && right.IsTruthy));
                break;
            case "||":
                frame.Push(CMUUtilityScriptValue.FromBool(left.IsTruthy || right.IsTruthy));
                break;
            default:
                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-value");
                break;
        }
    }

    public static void ExecuteUnary(CMUUtilityScriptFrame frame, string op)
    {
        if (!frame.TryPop(out var value))
            return;

        switch (op)
        {
            case "!":
                frame.Push(CMUUtilityScriptValue.FromBool(!value.IsTruthy));
                break;
            case "-":
                frame.Push(CMUUtilityScriptValue.FromNumber(-value.Number));
                break;
            default:
                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-value");
                break;
        }
    }

    public static bool EqualsValue(CMUUtilityScriptValue left, CMUUtilityScriptValue right)
    {
        if (left.Type == CMUUtilityScriptValueType.Null || right.Type == CMUUtilityScriptValueType.Null)
            return left.Type == right.Type;

        if (left.Type == CMUUtilityScriptValueType.Entity || right.Type == CMUUtilityScriptValueType.Entity)
            return left.Type == right.Type && left.Entity == right.Entity;

        if (left.Type == CMUUtilityScriptValueType.Number || right.Type == CMUUtilityScriptValueType.Number)
            return Math.Abs(left.Number - right.Number) < 0.0001;

        return string.Equals(left.AsText, right.AsText, StringComparison.Ordinal);
    }

    public static int CompareValue(CMUUtilityScriptValue left, CMUUtilityScriptValue right)
    {
        if (left.Type == CMUUtilityScriptValueType.Number || right.Type == CMUUtilityScriptValueType.Number)
            return left.Number.CompareTo(right.Number);

        return string.Compare(left.AsText, right.AsText, StringComparison.Ordinal);
    }
}
