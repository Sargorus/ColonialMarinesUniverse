using System.Linq;
using System.Text;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using static Content.Server.CMU14.Robotics.UtilityDrones.CMUUtilityScriptLexer;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Compiles the utility drone text language into a small jump-based bytecode.
/// Compilation is deterministic and bounded; runtime loops are separately
/// capped by the interpreter.
/// </summary>
public sealed class CMUUtilityScriptCompiler
{
    private static readonly HashSet<string> StatementKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "let",
        "set",
        "say",
        "wait",
        "move",
        "follow",
        "patrol",
        "return",
        "take",
        "put",
        "pull",
        "release",
        "switch",
        "drop",
        "stop",
        "if",
        "else",
        "while",
        "repeat",
        "on",
        "main",
        "use",
        "goto",
        "restart",
        "wield",
        "unwield",
        "aim",
        "shoot",
        "reload",
        "hold",
        "free",
        "fight",
        "disengage",
        "repair",
        "autorepair",
    };

    public bool TryCompile(string source, out CMUUtilityScriptProgram program, out string error)
    {
        program = new CMUUtilityScriptProgram();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(source))
        {
            error = Loc.GetString("cmu-utility-drone-script-error-empty");
            return false;
        }

        if (Encoding.UTF8.GetByteCount(source) > CMUUtilityDroneLimits.MaxProgramBytes)
        {
            error = Loc.GetString("cmu-utility-drone-script-error-too-large",
                ("max", CMUUtilityDroneLimits.MaxProgramBytes));
            return false;
        }

        try
        {
            var tokens = Tokenize(source);
            var parser = new Parser(tokens, program);
            parser.ParseProgram();

            if (!CMUUtilityScriptAnalyzer.TryAnalyze(program, out error))
                return false;

            return true;
        }
        catch (ScriptCompileException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private sealed class Parser
    {
        private static readonly Dictionary<string, int> Precedence = new(StringComparer.Ordinal)
        {
            ["||"] = 1,
            ["&&"] = 2,
            ["=="] = 3,
            ["!="] = 3,
            ["<"] = 4,
            ["<="] = 4,
            [">"] = 4,
            [">="] = 4,
            ["+"] = 5,
            ["-"] = 5,
            ["*"] = 6,
            ["/"] = 6,
            ["%"] = 6,
        };

        private readonly List<ScriptToken> _tokens;
        private readonly HashSet<string> _variables = new(StringComparer.Ordinal);
        private CMUUtilityScriptProgram _program;
        private int _index;
        private int _blockDepth;
        private int _expressionDepth;
        private int _temporary;
        private bool _inEventHandler;
        private bool _hasMainBlock;

        public Parser(List<ScriptToken> tokens, CMUUtilityScriptProgram program)
        {
            _tokens = tokens;
            _program = program;
        }

        public void ParseProgram()
        {
            while (Peek.Kind != ScriptTokenKind.End)
            {
                if (Is("on"))
                {
                    if (_hasMainBlock)
                    {
                        throw Fail(
                            "cmu-utility-drone-script-error-event-after-main",
                            Peek.Line);
                    }

                    ParseEventHandler();
                    continue;
                }

                if (Is("main"))
                {
                    if (_hasMainBlock)
                    {
                        throw Fail(
                            "cmu-utility-drone-script-error-main-duplicate",
                            Peek.Line);
                    }

                    Next();
                    _hasMainBlock = true;
                    _program.MainStart = _program.Instructions.Count;
                    ParseBlock();
                    Emit(CMUUtilityScriptOp.End);
                    continue;
                }

                if (_hasMainBlock)
                {
                    throw Fail(
                        "cmu-utility-drone-script-error-unexpected",
                        Peek.Line,
                        ("token", Peek.Text));
                }

                ParseStatement();
            }

            if (!_hasMainBlock)
                Emit(CMUUtilityScriptOp.End);
        }

        private void ParseEventHandler()
        {
            var line = Peek.Line;
            Expect("on");
            var eventName = ExpectIdentifier().ToLowerInvariant();
            var trigger = eventName switch
            {
                "damaged" or "damage" => CMUUtilityTrigger.OnDamaged,
                "heard" or "hear" => CMUUtilityTrigger.OnHear,
                _ => throw Fail(
                    "cmu-utility-drone-script-error-event",
                    line,
                    ("event", eventName)),
            };

            var phrase = string.Empty;
            if (trigger == CMUUtilityTrigger.OnHear)
            {
                if (Is("("))
                {
                    Next();
                    if (!Is(")"))
                        phrase = ExpectStringLiteral();

                    Expect(")");
                }
                else if (Peek.Kind == ScriptTokenKind.String)
                {
                    phrase = ExpectStringLiteral();
                }
            }

            foreach (var existing in _program.EventHandlers)
            {
                if (existing.Trigger != trigger ||
                    !string.Equals(existing.Phrase, phrase, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw Fail(
                    "cmu-utility-drone-script-error-event-duplicate",
                    line,
                    ("event", eventName));
            }

            var handler = new CMUUtilityScriptHandler
            {
                Trigger = trigger,
                Phrase = phrase,
            };
            var handlerProgram = handler.Program;
            var previous = _program;
            _program = handlerProgram;
            _inEventHandler = true;
            try
            {
                ParseBlock();
                Emit(CMUUtilityScriptOp.End);
            }
            finally
            {
                _inEventHandler = false;
                _program = previous;
            }

            handler.HasBlockingInstructions = handlerProgram.Instructions.Any(
                instruction => IsBlockingEventHandlerOp(instruction.Op));
            _program.EventHandlers.Add(handler);
        }

        private string ExpectStringLiteral()
        {
            var token = Peek;
            if (token.Kind != ScriptTokenKind.String)
            {
                throw Fail(
                    "cmu-utility-drone-script-error-expected",
                    token.Line,
                    ("expected", "quoted phrase"));
            }

            Next();
            return token.Text;
        }

        private static bool IsBlockingEventHandlerOp(CMUUtilityScriptOp op)
        {
            return op is
                CMUUtilityScriptOp.MoveEntity or
                CMUUtilityScriptOp.MoveCoordinates or
                CMUUtilityScriptOp.MoveDirection or
                CMUUtilityScriptOp.Follow or
                CMUUtilityScriptOp.Patrol or
                CMUUtilityScriptOp.Return or
                CMUUtilityScriptOp.TakeEntity or
                CMUUtilityScriptOp.TakeBackpack or
                CMUUtilityScriptOp.PutBackpack or
                CMUUtilityScriptOp.Pull or
                CMUUtilityScriptOp.Release or
                CMUUtilityScriptOp.Wait or
                CMUUtilityScriptOp.UseActiveOnTarget or
                CMUUtilityScriptOp.UseItemOnTarget or
                CMUUtilityScriptOp.Shoot or
                CMUUtilityScriptOp.ShootAt or
                CMUUtilityScriptOp.Fight or
                CMUUtilityScriptOp.Repair;
        }

        private void ParseStatement()
        {
            var token = Peek;
            if (token.Kind == ScriptTokenKind.Symbol && Is("{"))
            {
                ParseBlock();
                return;
            }

            if (token.Kind == ScriptTokenKind.Identifier &&
                PeekNext.Kind == ScriptTokenKind.Symbol &&
                PeekNext.Text == ":")
            {
                var label = Next().Text;
                Next();
                DefineLabel(label, token.Line);
                return;
            }

            if (token.Kind != ScriptTokenKind.Identifier)
                throw Fail("cmu-utility-drone-script-error-unexpected", token.Line, ("token", token.Text));

            switch (token.Text.ToLowerInvariant())
            {
                case "let":
                    ParseAssignment(define: true);
                    break;
                case "set":
                    ParseAssignment(define: false);
                    break;
                case "say":
                    Next();
                    ParseExpression();
                    Emit(CMUUtilityScriptOp.Say);
                    break;
                case "wait":
                    Next();
                    ParseExpression();
                    Emit(CMUUtilityScriptOp.Wait);
                    break;
                case "move":
                    Next();
                    ParseMove();
                    break;
                case "follow":
                    Next();
                    ParseFollow();
                    break;
                case "patrol":
                    Next();
                    ParsePatrol();
                    break;
                case "return":
                    Next();
                    Emit(_inEventHandler
                        ? CMUUtilityScriptOp.HandlerReturn
                        : CMUUtilityScriptOp.Return);
                    break;
                case "goto":
                    Next();
                    ParseGoto();
                    break;
                case "take":
                    Next();
                    ParseTake();
                    break;
                case "put":
                    Next();
                    ParsePut();
                    break;
                case "pull":
                    Next();
                    ParseExpression();
                    Emit(CMUUtilityScriptOp.Pull);
                    break;
                case "release":
                    Next();
                    Emit(CMUUtilityScriptOp.Release);
                    break;
                case "switch":
                    Next();
                    ParseSwitch();
                    break;
                case "drop":
                    Next();
                    Emit(CMUUtilityScriptOp.Drop);
                    break;
                case "use":
                    Next();
                    ParseUse();
                    break;
                case "wield":
                    Next();
                    ParseWield();
                    break;
                case "unwield":
                    Next();
                    Emit(CMUUtilityScriptOp.Unwield);
                    break;
                case "aim":
                    Next();
                    ParseAim();
                    break;
                case "shoot":
                    Next();
                    ParseShoot();
                    break;
                case "reload":
                    Next();
                    Emit(CMUUtilityScriptOp.Reload);
                    break;
                case "hold":
                    Next();
                    Expect("fire");
                    Emit(CMUUtilityScriptOp.HoldFire);
                    break;
                case "free":
                    Next();
                    Expect("fire");
                    Emit(CMUUtilityScriptOp.FreeFire);
                    break;
                case "fight":
                    Next();
                    ParseFight();
                    break;
                case "disengage":
                    Next();
                    Emit(CMUUtilityScriptOp.Disengage);
                    break;
                case "repair":
                case "autorepair":
                    Next();
                    ParseRepair();
                    break;
                case "restart":
                    Next();
                    Emit(CMUUtilityScriptOp.Restart);
                    break;
                case "stop":
                    Next();
                    Emit(CMUUtilityScriptOp.Stop);
                    break;
                case "if":
                    Next();
                    ParseIf();
                    break;
                case "while":
                    Next();
                    ParseWhile();
                    break;
                case "repeat":
                    Next();
                    ParseRepeat();
                    break;
                default:
                    throw Fail("cmu-utility-drone-script-error-unexpected", token.Line, ("token", token.Text));
            }
        }

        private void ParseAssignment(bool define)
        {
            Next();
            var name = ExpectIdentifier();
            if (define)
                DefineVariable(name);
            else if (!_variables.Contains(name))
                throw Fail("cmu-utility-drone-script-error-unknown-variable", Peek.Line, ("name", name));

            Expect("=");
            ParseExpression();
            Emit(CMUUtilityScriptOp.StoreVariable, text: name);
        }

        private void ParseGoto()
        {
            var first = ExpectIdentifier();
            var target = first;
            if (Is("."))
            {
                Next();
                target = $"{first}.{ExpectIdentifier()}";
            }

            Emit(CMUUtilityScriptOp.Goto, text: target);
        }

        private void ParseMove()
        {
            if (Is("coordinates") || Is("coords"))
            {
                Next();
                ParseExpression();
                ParseExpression();
                Emit(CMUUtilityScriptOp.MoveCoordinates);
                return;
            }

            if (Is("up") || Is("down") || Is("left") || Is("right") ||
                Is("north") || Is("south") || Is("east") || Is("west"))
            {
                var direction = Next().Text.ToLowerInvariant();
                EmitString(direction);
                if (AtStatementBoundary())
                    EmitNumber(5);
                else
                    ParseExpression();
                Emit(CMUUtilityScriptOp.MoveDirection);
                return;
            }

            if (Is("marker"))
            {
                Next();
                ParseExpression();
                EmitCall("marker", 1);
                Emit(CMUUtilityScriptOp.MoveEntity);
                return;
            }

            if (Is("to") || Is("entity"))
            {
                Next();
                ParseExpression();
                Emit(CMUUtilityScriptOp.MoveEntity);
                return;
            }

            if (Is("item"))
            {
                Next();
                ParseExpression();
                ParseOptionalRadiusArgument();
                EmitCall("item", 2);
                Emit(CMUUtilityScriptOp.MoveEntity);
                return;
            }

            if (Is("hand"))
            {
                Next();
                ParseHandSelector();
                EmitCall("hand", 1);
                Emit(CMUUtilityScriptOp.MoveEntity);
                return;
            }

            ParseExpression();
            Emit(CMUUtilityScriptOp.MoveEntity);
        }

        private void ParseFollow()
        {
            if (AtStatementBoundary())
                EmitNumber(0);
            else
                ParseExpression();

            Emit(CMUUtilityScriptOp.Follow);
        }

        private void ParsePatrol()
        {
            Expect("marker");
            ParseExpression();
            EmitCall("marker", 1);
            if (Is("radius"))
            {
                Next();
                ParseExpression();
            }
            else
            {
                EmitNumber(4);
            }

            if (Is("seconds"))
            {
                Next();
                ParseExpression();
            }
            else
            {
                EmitNumber(1);
            }

            Emit(CMUUtilityScriptOp.Patrol);
        }

        private void ParseTake()
        {
            if (Is("from"))
            {
                Next();
                Expect("backpack");
                ParseExpression();
                var hand = ParseOptionalHand();
                Emit(CMUUtilityScriptOp.TakeBackpack, text: hand);
                return;
            }

            if (Is("item"))
            {
                Next();
                ParseExpression();
                ParseOptionalRadiusArgument();
                EmitCall("item", 2);
                var hand = ParseOptionalHand();
                Emit(CMUUtilityScriptOp.TakeEntity, text: hand);
                return;
            }

            ParseExpression();
            var handId = ParseOptionalHand();
            Emit(CMUUtilityScriptOp.TakeEntity, text: handId);
        }

        private string ParseOptionalHand()
        {
            if (!Is("into"))
                return string.Empty;

            Next();
            var hand = ExpectIdentifier().ToLowerInvariant();
            if (hand is not ("left" or "right"))
                throw Fail("cmu-utility-drone-script-error-hand", Peek.Line, ("value", hand));

            return hand;
        }

        private void ParsePut()
        {
            Expect("in");
            Expect("backpack");
            ParseExpression();
            Emit(CMUUtilityScriptOp.PutBackpack);
        }

        private void ParseUse()
        {
            if (Is("self"))
            {
                Next();
                Emit(CMUUtilityScriptOp.UseSelf);
                return;
            }

            if (Is("on"))
            {
                Next();
                ParseExpression();
                Emit(CMUUtilityScriptOp.UseActiveOnTarget);
                return;
            }

            ParseExpression();
            Expect("on");
            ParseExpression();
            Emit(CMUUtilityScriptOp.UseItemOnTarget);
        }

        private void ParseWield()
        {
            if (AtStatementBoundary())
                EmitString(string.Empty);
            else
                ParseExpression();

            Emit(CMUUtilityScriptOp.Wield);
        }

        private void ParseAim()
        {
            if (Is("at"))
            {
                Next();
                ParseExpression();
                Emit(CMUUtilityScriptOp.AimEntity);
                return;
            }

            if (Is("coords") || Is("coordinates"))
            {
                Next();
                ParseExpression();
                ParseExpression();
                Emit(CMUUtilityScriptOp.AimCoordinates);
                return;
            }

            Emit(CMUUtilityScriptOp.AimClear);
        }

        private void ParseShoot()
        {
            if (AtStatementBoundary())
            {
                EmitNumber(1);
                Emit(CMUUtilityScriptOp.Shoot);
                return;
            }

            if (Is("at"))
            {
                Next();
                ParseExpression();
                EmitNumber(1);
                Emit(CMUUtilityScriptOp.ShootAt);
                return;
            }

            ParseExpression();
            Emit(CMUUtilityScriptOp.Shoot);
        }

        private void ParseFight()
        {
            var auto = true;
            if (!AtStatementBoundary() && !Is("within"))
            {
                ParseExpression();
                auto = false;
            }

            if (Is("within"))
            {
                Next();
                ParseExpression();
            }
            else
            {
                EmitNumber(12);
            }

            Emit(CMUUtilityScriptOp.Fight, text: auto ? "auto" : "target");
        }

        private void ParseRepair()
        {
            var mode = "auto";
            if (Is("self"))
            {
                Next();
                mode = "self";
            }
            else if (Is("ally"))
            {
                Next();
                mode = "ally";
                if (!AtStatementBoundary() && !Is("within"))
                {
                    ParseExpression();
                    mode = "target";
                }
            }
            else if (!AtStatementBoundary() && !Is("within"))
            {
                ParseExpression();
                mode = "target";
            }

            if (Is("within"))
            {
                Next();
                ParseExpression();
            }
            else
            {
                EmitNumber(8);
            }

            Emit(CMUUtilityScriptOp.Repair, text: mode);
        }

        private void ParseSwitch()
        {
            var side = ExpectIdentifier().ToLowerInvariant();
            if (side is not ("left" or "right"))
                throw Fail("cmu-utility-drone-script-error-hand", Peek.Line, ("value", side));

            EmitString(side);
            Emit(CMUUtilityScriptOp.SwitchHand);
        }

        private void ParseIf()
        {
            ParseExpression();
            var jumpFalse = Emit(CMUUtilityScriptOp.JumpIfFalse);
            ParseBlock();

            if (Is("else"))
            {
                Next();
                var jumpEnd = Emit(CMUUtilityScriptOp.Jump);
                _program.Instructions[jumpFalse].Jump = _program.Instructions.Count;
                ParseBlock();
                _program.Instructions[jumpEnd].Jump = _program.Instructions.Count;
            }
            else
            {
                _program.Instructions[jumpFalse].Jump = _program.Instructions.Count;
            }
        }

        private void ParseWhile()
        {
            var start = _program.Instructions.Count;
            ParseExpression();
            var jumpFalse = Emit(CMUUtilityScriptOp.JumpIfFalse);
            ParseBlock();
            Emit(CMUUtilityScriptOp.Jump, jump: start);
            _program.Instructions[jumpFalse].Jump = _program.Instructions.Count;
        }

        private void ParseRepeat()
        {
            ParseExpression();
            var temp = $"$repeat{_temporary++}";
            Emit(CMUUtilityScriptOp.StoreVariable, text: temp);

            var start = _program.Instructions.Count;
            Emit(CMUUtilityScriptOp.LoadVariable, text: temp);
            EmitNumber(0);
            Emit(CMUUtilityScriptOp.Binary, text: ">");
            var jumpFalse = Emit(CMUUtilityScriptOp.JumpIfFalse);

            ParseBlock();

            Emit(CMUUtilityScriptOp.LoadVariable, text: temp);
            EmitNumber(1);
            Emit(CMUUtilityScriptOp.Binary, text: "-");
            Emit(CMUUtilityScriptOp.StoreVariable, text: temp);
            Emit(CMUUtilityScriptOp.Jump, jump: start);
            _program.Instructions[jumpFalse].Jump = _program.Instructions.Count;
        }

        private void ParseBlock()
        {
            if (_blockDepth >= CMUUtilityDroneLimits.MaxScriptBlockDepth)
            {
                throw Fail("cmu-utility-drone-script-error-depth",
                    Peek.Line,
                    ("max", CMUUtilityDroneLimits.MaxScriptBlockDepth));
            }

            Expect("{");
            _blockDepth++;
            try
            {
                while (!Is("}"))
                {
                    if (Peek.Kind == ScriptTokenKind.End)
                        throw Fail("cmu-utility-drone-script-error-unexpected", Peek.Line, ("token", "end of script"));

                    ParseStatement();
                }

                Expect("}");
            }
            finally
            {
                _blockDepth--;
            }
        }

        private void ParseExpression(int minimumPrecedence = 0)
        {
            ParseUnary();

            while (Peek.Kind == ScriptTokenKind.Symbol &&
                   Precedence.TryGetValue(Peek.Text, out var precedence) &&
                   precedence >= minimumPrecedence)
            {
                var op = Next().Text;
                ParseExpression(precedence + 1);
                Emit(CMUUtilityScriptOp.Binary, text: op);
            }
        }

        private void ParseUnary()
        {
            if (_expressionDepth++ > 64)
            {
                _expressionDepth--;
                throw Fail("cmu-utility-drone-script-error-depth",
                    Peek.Line,
                    ("max", 64));
            }

            try
            {
                if (Is("!") || Is("-"))
                {
                    var op = Next().Text;
                    ParseUnary();
                    Emit(CMUUtilityScriptOp.Unary, text: op);
                    return;
                }

                ParsePrimary();
            }
            finally
            {
                _expressionDepth--;
            }
        }

        private void ParsePrimary()
        {
            var token = Peek;
            switch (token.Kind)
            {
                case ScriptTokenKind.Number:
                    Next();
                    EmitNumber(token.Number);
                    return;

                case ScriptTokenKind.String:
                    Next();
                    EmitString(token.Text);
                    return;

                case ScriptTokenKind.Symbol when token.Text == "(":
                    Next();
                    ParseExpression();
                    Expect(")");
                    return;

                case ScriptTokenKind.Identifier:
                    Next();
                    if (Is("("))
                    {
                        ParseFunctionCall(token.Text);
                        return;
                    }

                    if (token.Text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        token.Text.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        Emit(CMUUtilityScriptOp.PushBool,
                            number: token.Text.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                        return;
                    }

                    if (!_variables.Contains(token.Text))
                    {
                        throw Fail("cmu-utility-drone-script-error-unknown-variable",
                            token.Line,
                            ("name", token.Text));
                    }

                    Emit(CMUUtilityScriptOp.LoadVariable, text: token.Text);
                    return;

                default:
                    throw Fail("cmu-utility-drone-script-error-unexpected", token.Line, ("token", token.Text));
            }
        }

        private void ParseFunctionCall(string name)
        {
            Expect("(");
            var count = 0;
            if (!Is(")"))
            {
                while (true)
                {
                    ParseExpression();
                    count++;
                    if (!Is(","))
                        break;

                    Next();
                }
            }

            Expect(")");
            EmitCall(name, count);
        }

        private void ParseOptionalRadiusArgument()
        {
            if (Is("within"))
            {
                Next();
                ParseExpression();
            }
            else
            {
                EmitNumber(8);
            }
        }

        private void ParseHandSelector()
        {
            var hand = ExpectIdentifier().ToLowerInvariant();
            if (hand is not ("left" or "right"))
                throw Fail("cmu-utility-drone-script-error-hand", Peek.Line, ("value", hand));

            EmitString(hand);
        }

        private void DefineLabel(string name, int line)
        {
            if (_program.Labels.ContainsKey(name))
            {
                throw Fail(
                    "cmu-utility-drone-script-error-label-duplicate",
                    line,
                    ("label", name));
            }

            _program.Labels[name] = _program.Instructions.Count;
        }

        private void DefineVariable(string name)
        {
            if (_variables.Contains(name))
                return;

            if (_variables.Count >= CMUUtilityDroneLimits.MaxScriptVariables)
            {
                throw Fail("cmu-utility-drone-script-error-variables",
                    Peek.Line,
                    ("max", CMUUtilityDroneLimits.MaxScriptVariables));
            }

            _variables.Add(name);
        }

        private string ExpectIdentifier()
        {
            var token = Peek;
            if (token.Kind != ScriptTokenKind.Identifier)
                throw Fail("cmu-utility-drone-script-error-identifier", token.Line);

            Next();
            return token.Text;
        }

        private void Expect(string text)
        {
            if (!Is(text))
                throw Fail("cmu-utility-drone-script-error-expected", Peek.Line, ("expected", text));

            Next();
        }

        private bool Is(string text)
        {
            return string.Equals(Peek.Text, text, StringComparison.OrdinalIgnoreCase);
        }

        private bool AtStatementBoundary()
        {
            return Peek.Kind == ScriptTokenKind.End ||
                   Is("}") ||
                   (Peek.Kind == ScriptTokenKind.Identifier && StatementKeywords.Contains(Peek.Text));
        }

        private ScriptToken Peek => _tokens[_index];

        private ScriptToken PeekNext =>
            _index + 1 < _tokens.Count ? _tokens[_index + 1] : _tokens[^1];

        private ScriptToken Next()
        {
            var token = _tokens[_index];
            if (_index < _tokens.Count - 1)
                _index++;

            return token;
        }

        private int Emit(CMUUtilityScriptOp op, string text = "", double number = 0, int jump = 0, int argCount = 0)
        {
            if (_program.Instructions.Count >= CMUUtilityDroneLimits.MaxScriptInstructions)
            {
                throw Fail("cmu-utility-drone-script-error-instructions",
                    Peek.Line,
                    ("max", CMUUtilityDroneLimits.MaxScriptInstructions));
            }

            _program.Instructions.Add(new CMUUtilityScriptInstruction
            {
                Op = op,
                Text = text,
                Number = number,
                Jump = jump,
                ArgCount = argCount,
                Line = Peek.Line,
            });
            return _program.Instructions.Count - 1;
        }

        private void EmitNumber(double number)
        {
            Emit(CMUUtilityScriptOp.PushNumber, number: number);
        }

        private void EmitString(string text)
        {
            Emit(CMUUtilityScriptOp.PushString, text: text);
        }

        private void EmitCall(string name, int count)
        {
            if (name.Equals("marker", StringComparison.OrdinalIgnoreCase))
                _program.UsesMarkers = true;

            Emit(CMUUtilityScriptOp.Call, text: name.ToLowerInvariant(), argCount: count);
        }
    }
}
