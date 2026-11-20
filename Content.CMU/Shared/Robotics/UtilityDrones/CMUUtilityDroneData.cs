using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Robotics.UtilityDrones;

[Serializable, NetSerializable]
public enum CMUUtilityDroneUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum CMUUtilityStepKind : byte
{
    MoveTo,
    Guard,
    Follow,
    Patrol,
    Fetch,
    Pull,
    StopPull,
    Deliver,
    Repair,
    Report,
    Wait,
    Return,
}

[Serializable, NetSerializable]
public enum CMUUtilityTrigger : byte
{
    OnDamaged,
    OnSee,
    OnTaskFailed,
    OnLowSupplies,
    OnStartedPulling,
    OnStoppedPulling,
    OnHear,
}

[Serializable, NetSerializable]
public enum CMUUtilityRuleAction : byte
{
    Return,
    Report,
}

[Serializable, NetSerializable]
public enum CMUUtilityConsoleCommand : byte
{
    SaveProgram,
    PrintBoard,
    UnlinkMarker,
    ScanItems,
}

[Serializable, NetSerializable]
public enum CMUUtilityDroneStatus : byte
{
    Idle,
    Running,
    Paused,
    Returning,
    Failed,
}

public static class CMUUtilityDroneLimits
{
    public const int MaxDrones = 15;
    public const int MaxSteps = 16;
    public const int MaxRules = 8;
    public const int MaxMarkers = 32;
    public const int MaxWaypoints = 8;
    public const float MinRadius = 1f;
    public const float MaxRadius = 32f;
    public const int MaxStringLength = 128;
    public const int MaxProgramBytes = 8 * 1024;
    public const int ThinkBudgetPerTick = 32;
    public const int MaxScanCandidates = 32;
    public const int MaxPathAttempts = 3;
    public const int MaxScriptInstructions = 4096;
    public const int MaxScriptInstructionsPerTick = 64;
    public const int MaxScriptBlockDepth = 8;
    public const int MaxScriptVariables = 32;
    public const int MaxScriptStringLength = 256;
    public const int MaxInfoEntries = 8;
    public const int MaxInfoTextLength = 320;
    public const int MaxInfoCandidates = 96;
    public const int MaxEventHandlerInstructions = 512;
    public const int MaxQueuedEvents = 16;
    public const int MaxQueuedEventSpeech = 8;
}

[Serializable, NetSerializable]
public sealed record CMUUtilityStepInfo(
    CMUUtilityStepKind Kind,
    NetEntity Marker,
    float Radius,
    float Seconds,
    string Note,
    NetEntity Target = default);

[Serializable, NetSerializable]
public sealed record CMUUtilityRuleInfo(
    CMUUtilityTrigger Trigger,
    CMUUtilityRuleAction Action,
    string Message = "");

[Serializable, NetSerializable]
public sealed record CMUUtilityItemInfo(
    NetEntity Entity,
    string Label,
    bool Item,
    bool Pullable);

[Serializable, NetSerializable]
public sealed class CMUUtilityProgramInfo
{
    public List<CMUUtilityStepInfo> Steps = new();
    public List<CMUUtilityRuleInfo> Rules = new();
    public bool Repeat;
    public float RepeatCooldown = 30f;
    public string Script = string.Empty;

    public CMUUtilityProgramInfo()
    {
    }

    public CMUUtilityProgramInfo(
        List<CMUUtilityStepInfo> steps,
        List<CMUUtilityRuleInfo> rules,
        bool repeat,
        float repeatCooldown)
    {
        Steps = steps;
        Rules = rules;
        Repeat = repeat;
        RepeatCooldown = repeatCooldown;
    }
}

[Serializable, NetSerializable]
public sealed record CMUUtilityMarkerInfo(NetEntity Marker, bool Anchored);

[Serializable, NetSerializable]
public sealed class CMUUtilityDroneConsoleState : BoundUserInterfaceState
{
    public readonly List<CMUUtilityMarkerInfo> Markers;
    public readonly CMUUtilityProgramInfo Program;
    public readonly string Status;
    public readonly string ValidationError;
    public readonly List<CMUUtilityItemInfo> NearbyItems;
    public readonly NetEntity ScannedMarker;
    public readonly string Script;

    public CMUUtilityDroneConsoleState(
        List<CMUUtilityMarkerInfo> markers,
        CMUUtilityProgramInfo program,
        string status,
        string validationError,
        List<CMUUtilityItemInfo>? nearbyItems = null,
        NetEntity scannedMarker = default,
        string script = "")
    {
        Markers = markers;
        Program = program;
        Status = status;
        ValidationError = validationError;
        NearbyItems = nearbyItems ?? new List<CMUUtilityItemInfo>();
        ScannedMarker = scannedMarker;
        Script = script;
    }
}

[Serializable, NetSerializable]
public sealed class CMUUtilityConsoleCommandMessage : BoundUserInterfaceMessage
{
    public readonly CMUUtilityConsoleCommand Command;
    public readonly CMUUtilityProgramInfo Program;
    public readonly NetEntity Marker;

    public CMUUtilityConsoleCommandMessage(
        CMUUtilityConsoleCommand command,
        CMUUtilityProgramInfo program,
        NetEntity marker = default)
    {
        Command = command;
        Program = program;
        Marker = marker;
    }
}

[Serializable, NetSerializable]
public sealed partial class CMUUtilityDroneInstallBoardDoAfterEvent : SimpleDoAfterEvent;

[DataDefinition]
public sealed partial class CMUUtilityStep
{
    [DataField]
    public CMUUtilityStepKind Kind;

    [DataField]
    public EntityUid? Marker;

    [DataField]
    public EntityUid? Target;

    [DataField]
    public float Radius = 4f;

    [DataField]
    public float Seconds = 5f;

    [DataField]
    public string Note = string.Empty;
}

[DataDefinition]
public sealed partial class CMUUtilityRule
{
    [DataField]
    public CMUUtilityTrigger Trigger;

    [DataField]
    public CMUUtilityRuleAction Action;

    [DataField]
    public string Message = string.Empty;
}

[DataDefinition]
public sealed partial class CMUUtilityProgram
{
    [DataField]
    public List<CMUUtilityStep> Steps = new();

    [DataField]
    public List<CMUUtilityRule> Rules = new();

    [DataField]
    public bool Repeat;

    [DataField]
    public float RepeatCooldown = 30f;

    public CMUUtilityProgram Clone()
    {
        var clone = new CMUUtilityProgram
        {
            Repeat = Repeat,
            RepeatCooldown = RepeatCooldown,
        };

        foreach (var step in Steps)
        {
            clone.Steps.Add(new CMUUtilityStep
            {
                Kind = step.Kind,
                Marker = step.Marker,
                Target = step.Target,
                Radius = step.Radius,
                Seconds = step.Seconds,
                Note = step.Note,
            });
        }

        foreach (var rule in Rules)
        {
            clone.Rules.Add(new CMUUtilityRule
            {
                Trigger = rule.Trigger,
                Action = rule.Action,
                Message = rule.Message,
            });
        }

        return clone;
    }
}
