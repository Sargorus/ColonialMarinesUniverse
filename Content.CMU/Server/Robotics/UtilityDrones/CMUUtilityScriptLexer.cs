using System.Globalization;
using System.Text;
using Content.Shared.CMU14.Robotics.UtilityDrones;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

internal static class CMUUtilityScriptLexer
{
    public static List<ScriptToken> Tokenize(string source)
    {
        var tokens = new List<ScriptToken>();
        var index = 0;
        var line = 1;
        while (index < source.Length)
        {
            var character = source[index];
            if (character == '\n')
            {
                line++;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (character == '#')
            {
                while (index < source.Length && source[index] != '\n')
                    index++;
                continue;
            }

            if (character == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n')
                    index++;
                continue;
            }

            if (char.IsLetter(character) || character == '_')
            {
                var start = index++;
                while (index < source.Length &&
                       (char.IsLetterOrDigit(source[index]) || source[index] == '_'))
                {
                    index++;
                }

                tokens.Add(new ScriptToken(
                    ScriptTokenKind.Identifier,
                    source[start..index],
                    0,
                    line));
                continue;
            }

            if (char.IsDigit(character) ||
                (character == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1])))
            {
                var start = index++;
                var seenDot = character == '.';
                while (index < source.Length)
                {
                    var next = source[index];
                    if (char.IsDigit(next))
                    {
                        index++;
                        continue;
                    }

                    if (next == '.' && !seenDot)
                    {
                        seenDot = true;
                        index++;
                        continue;
                    }

                    break;
                }

                var text = source[start..index];
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    !double.IsFinite(number))
                {
                    throw Fail("cmu-utility-drone-script-error-number", line, ("text", text));
                }

                tokens.Add(new ScriptToken(ScriptTokenKind.Number, text, number, line));
                continue;
            }

            if (character == '"')
            {
                index++;
                var builder = new StringBuilder();
                var closed = false;
                while (index < source.Length)
                {
                    var next = source[index++];
                    if (next == '"')
                    {
                        closed = true;
                        break;
                    }

                    if (next == '\n')
                        line++;

                    if (next == '\\' && index < source.Length)
                    {
                        var escaped = source[index++];
                        builder.Append(escaped switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            _ => escaped,
                        });
                    }
                    else
                    {
                        builder.Append(next);
                    }
                }

                if (!closed)
                    throw Fail("cmu-utility-drone-script-error-string", line);

                if (builder.Length > CMUUtilityDroneLimits.MaxScriptStringLength)
                {
                    throw Fail("cmu-utility-drone-script-error-string-long",
                        line,
                        ("max", CMUUtilityDroneLimits.MaxScriptStringLength));
                }

                tokens.Add(new ScriptToken(ScriptTokenKind.String, builder.ToString(), 0, line));
                continue;
            }

            string? symbol = null;
            if (index + 1 < source.Length)
            {
                var pair = source.Substring(index, 2);
                if (pair is "==" or "!=" or "<=" or ">=" or "&&" or "||")
                {
                    symbol = pair;
                    index += 2;
                }
            }

            if (symbol == null &&
                character is '{' or '}' or '(' or ')' or ',' or '=' or '+' or '-' or '*' or '/' or '%' or '<' or '>' or '!' or ':' or '.')
            {
                symbol = character.ToString();
                index++;
            }

            if (symbol == null)
            {
                throw Fail("cmu-utility-drone-script-error-character",
                    line,
                    ("character", character));
            }

            tokens.Add(new ScriptToken(ScriptTokenKind.Symbol, symbol, 0, line));
        }

        tokens.Add(new ScriptToken(ScriptTokenKind.End, string.Empty, 0, line));
        return tokens;
    }

    public static ScriptCompileException Fail(string key, int line, params (string, object)[] args)
    {
        var arguments = new (string, object)[args.Length + 1];
        arguments[0] = ("line", line);
        for (var i = 0; i < args.Length; i++)
            arguments[i + 1] = args[i];

        return new ScriptCompileException(Loc.GetString(key, arguments));
    }

    public enum ScriptTokenKind
    {
        End,
        Identifier,
        Number,
        String,
        Symbol,
    }

    public readonly record struct ScriptToken(
        ScriptTokenKind Kind,
        string Text,
        double Number,
        int Line);

    public sealed class ScriptCompileException(string message) : Exception(message);

}