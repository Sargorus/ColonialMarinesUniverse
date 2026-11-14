using Content.Shared.CMU14.Robotics.UtilityDrones;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public enum CMUUtilityScriptOp : byte
{
    PushNumber,
    PushString,
    PushBool,
    LoadVariable,
    StoreVariable,
    Binary,
    Unary,
    Call,
    Jump,
    JumpIfFalse,
    Say,
    Wait,
    MoveEntity,
    Follow,
    Patrol,
    Return,
    TakeEntity,
    TakeBackpack,
    PutBackpack,
    Pull,
    Release,
    SwitchHand,
    Drop,
    MoveCoordinates,
    MoveDirection,
    UseSelf,
    UseActiveOnTarget,
    UseItemOnTarget,
    Wield,
    Unwield,
    AimEntity,
    AimCoordinates,
    AimClear,
    Shoot,
    ShootAt,
    Reload,
    HoldFire,
    FreeFire,
    Fight,
    Disengage,
    Repair,
    Goto,
    HandlerReturn,
    Restart,
    Stop,
    End,
}

public sealed class CMUUtilityScriptInstruction
{
    public CMUUtilityScriptOp Op;
    public string Text = string.Empty;
    public double Number;
    public int Jump;
    public int ArgCount;
    public int Line;
}

public sealed class CMUUtilityScriptProgram
{
    public readonly List<CMUUtilityScriptInstruction> Instructions = new();
    public readonly Dictionary<string, int> Labels = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<CMUUtilityScriptHandler> EventHandlers = new();
    public int MainStart;
    public int MaxStack;
    public bool UsesMarkers;

    public bool HasEventHandler(CMUUtilityTrigger trigger)
    {
        foreach (var handler in EventHandlers)
        {
            if (handler.Trigger == trigger)
                return true;
        }

        return false;
    }
}

public sealed class CMUUtilityScriptHandler
{
    public CMUUtilityTrigger Trigger;
    public string Phrase = string.Empty;
    public CMUUtilityScriptProgram Program = new();
    public bool HasBlockingInstructions;
}

public readonly record struct CMUUtilityScriptEvent(
    CMUUtilityTrigger Trigger,
    EntityUid? Subject,
    string Text);

public enum CMUUtilityScriptValueType : byte
{
    Null,
    Number,
    String,
    Bool,
    Entity,
}

public readonly record struct CMUUtilityScriptValue(
    CMUUtilityScriptValueType Type,
    double Number,
    string Text,
    bool Bool,
    EntityUid Entity)
{
    public static CMUUtilityScriptValue Null => new(CMUUtilityScriptValueType.Null, 0, string.Empty, false, default);
    public static CMUUtilityScriptValue FromNumber(double value) => new(CMUUtilityScriptValueType.Number, value, string.Empty, false, default);
    public static CMUUtilityScriptValue FromString(string value) => new(CMUUtilityScriptValueType.String, 0, value, false, default);
    public static CMUUtilityScriptValue FromBool(bool value) => new(CMUUtilityScriptValueType.Bool, 0, string.Empty, value, default);
    public static CMUUtilityScriptValue FromEntity(EntityUid value) => new(CMUUtilityScriptValueType.Entity, 0, string.Empty, false, value);

    public bool IsTruthy => Type switch
    {
        CMUUtilityScriptValueType.Bool => Bool,
        CMUUtilityScriptValueType.Number => Number != 0,
        CMUUtilityScriptValueType.String => !string.IsNullOrEmpty(Text),
        CMUUtilityScriptValueType.Entity => Entity.IsValid(),
        _ => false,
    };

    public string AsText => Type switch
    {
        CMUUtilityScriptValueType.Number => Number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        CMUUtilityScriptValueType.String => Text,
        CMUUtilityScriptValueType.Bool => Bool ? "true" : "false",
        CMUUtilityScriptValueType.Entity => Entity.ToString(),
        _ => string.Empty,
    };
}

public enum CMUUtilityScriptActionKind : byte
{
    Say,
    Wait,
    Move,
    MoveCoordinates,
    Follow,
    Patrol,
    Take,
    TakeBackpack,
    PutBackpack,
    Pull,
    Release,
    Return,
    Use,
    Shoot,
    Fight,
    Repair,
}

/// <summary>
/// Marker for action-state objects. The VM stores one action per frame and
/// ticks it through the owning system.
/// </summary>
public interface ICMUUtilityScriptAction
{
    CMUUtilityScriptActionKind Kind { get; }
}

/// <summary>
/// Data needed to resume one long-running script action. The system owns the
/// behavior; this object owns only the resumable state.
/// </summary>
public sealed class CMUUtilityScriptActionState : ICMUUtilityScriptAction
{
    public CMUUtilityScriptActionKind Kind { get; }
    public EntityUid? Target;
    public EntityUid? Item;
    public EntityCoordinates? Coordinates;
    public string Text = string.Empty;
    public string Hand = string.Empty;
    public float Radius = 4f;
    public float Seconds;
    public int Phase;
    public int Counter;
    public int Attempts;
    public bool AutoTarget;
    public TimeSpan Started;
    public TimeSpan WaitUntil;

    public CMUUtilityScriptActionState(CMUUtilityScriptActionKind kind)
    {
        Kind = kind;
    }
}

/// <summary>
/// One execution frame. Main and each event handler have their own program
/// counter and expression stack; variables and markers belong to the shared
/// execution so handlers and main observe the same values.
/// </summary>
public sealed class CMUUtilityScriptFrame
{
    public readonly CMUUtilityScriptExecution Execution;
    public readonly CMUUtilityScriptProgram Program;
    public readonly List<CMUUtilityScriptValue> Stack = new();

    public EntityUid? EventSubject;
    public string EventText = string.Empty;
    public bool EventContext;
    public string GotoMainLabel = string.Empty;
    public bool RestartRequested;
    public bool StopRequested;
    public int Pc;
    public CMUUtilityScriptActionState? Action;
    public int Instructions;
    public bool Ended;
    public string Failure = string.Empty;

    public CMUUtilityScriptFrame(
        CMUUtilityScriptExecution execution,
        CMUUtilityScriptProgram program)
    {
        Execution = execution;
        Program = program;
    }

    public bool Failed => !string.IsNullOrEmpty(Failure);

    public List<EntityUid> Markers => Execution.Markers;

    public Dictionary<string, CMUUtilityScriptValue> Variables => Execution.Variables;

    public void Push(CMUUtilityScriptValue value)
    {
        if (Stack.Count >= 128)
        {
            Failure = Loc.GetString("cmu-utility-drone-script-fail-stack");
            return;
        }

        Stack.Add(value);
    }

    public bool TryPop(out CMUUtilityScriptValue value)
    {
        value = CMUUtilityScriptValue.Null;
        if (Stack.Count == 0)
        {
            Failure = Loc.GetString("cmu-utility-drone-script-fail-stack");
            return false;
        }

        var index = Stack.Count - 1;
        value = Stack[index];
        Stack.RemoveAt(index);
        return true;
    }

    public bool TryPopNumber(out double value)
    {
        value = 0;
        if (!TryPop(out var popped))
            return false;

        if (popped.Type != CMUUtilityScriptValueType.Number ||
            !double.IsFinite(popped.Number))
        {
            Failure = Loc.GetString("cmu-utility-drone-script-fail-value");
            return false;
        }

        value = popped.Number;
        return true;
    }

    public bool TryPopEntity(out EntityUid entity)
    {
        entity = default;
        if (!TryPop(out var popped) ||
            popped.Type != CMUUtilityScriptValueType.Entity ||
            !popped.Entity.IsValid())
        {
            Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
            return false;
        }

        entity = popped.Entity;
        return true;
    }
}

/// <summary>
/// Server-only execution state for a text program. It is held by the
/// server-side drone runtime component and is never serialized or networked.
/// The main frame and the single active event frame share variables, markers
/// and event queues.
/// </summary>
public sealed class CMUUtilityScriptExecution
{
    public readonly List<EntityUid> Markers;
    public readonly Dictionary<string, CMUUtilityScriptValue> Variables;
    public readonly Queue<CMUUtilityScriptEvent> Events = new();
    public readonly Queue<string> EventSpeech = new();
    public readonly CMUUtilityScriptFrame Main;

    public CMUUtilityScriptFrame? ActiveHandler;

    public CMUUtilityScriptProgram Program => Main.Program;

    public CMUUtilityScriptExecution()
        : this(new CMUUtilityScriptProgram())
    {
    }

    public CMUUtilityScriptExecution(
        CMUUtilityScriptProgram program,
        Dictionary<string, CMUUtilityScriptValue>? variables = null,
        List<EntityUid>? markers = null)
    {
        Variables = variables ?? new Dictionary<string, CMUUtilityScriptValue>();
        Markers = markers ?? new List<EntityUid>();
        Main = new CMUUtilityScriptFrame(this, program);
    }

    public void EnqueueEvent(
        CMUUtilityTrigger trigger,
        EntityUid? subject,
        string text = "")
    {
        if (Events.Count >= CMUUtilityDroneLimits.MaxQueuedEvents)
            return;

        foreach (var queued in Events)
        {
            if (queued.Trigger == trigger &&
                queued.Subject == subject &&
                string.Equals(queued.Text, text, StringComparison.Ordinal))
            {
                return;
            }
        }

        Events.Enqueue(new CMUUtilityScriptEvent(trigger, subject, text));
    }

    public void EnqueueEventSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            EventSpeech.Count >= CMUUtilityDroneLimits.MaxQueuedEventSpeech)
        {
            return;
        }

        EventSpeech.Enqueue(text);
    }
}
