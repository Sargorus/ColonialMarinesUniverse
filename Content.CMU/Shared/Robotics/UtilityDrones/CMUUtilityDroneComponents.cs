using System.Numerics;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Console-side authority for a task-marker registry and the program that will
/// be printed onto a physical directive board.
/// </summary>
[RegisterComponent]
public sealed partial class CMUUtilityDroneConsoleComponent : Component
{
    public readonly List<EntityUid> Markers = new();
    public readonly List<EntityUid> ScanItems = new();

    [DataField]
    public CMUUtilityProgram Program = new();

    [DataField]
    public string Script = string.Empty;

    public EntityUid? ScanMarker;
    public TimeSpan NextCommand;
    public TimeSpan NextSave;
    public TimeSpan NextUi;

    public long RejectedCommands;
    public long RejectedSaves;

    public string Status = string.Empty;
    public string ValidationError = string.Empty;
}

/// <summary>
/// Per-drone program runtime. A program is copied from an installed directive
/// board so editing or removing the source console cannot mutate a live run.
/// </summary>
[RegisterComponent]
public sealed partial class CMUUtilityDroneAgentComponent : Component
{
    [DataField]
    public float SightRange = 10f;

    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.5);

    [DataField]
    public TimeSpan BoardInstallDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public EntProtoId HomeAnchorPrototype = "CMUUtilityDroneHomeAnchor";

    [DataField]
    public string ProgramBoardContainerId = "cmu-drone-program-board";

    [DataField]
    public bool ProgrammableMode;

    [DataField]
    public bool BoardPortLocked = true;

    public EntityUid? ProgramBoard;
    public ContainerSlot? ProgramBoardContainer;
    public EntityUid? BoardInstaller;
    public EntityUid? HomeAnchor;
    public EntityUid? FollowTarget;

    public int Number;
    public CMUUtilityDroneStatus Status = CMUUtilityDroneStatus.Idle;
    public string Activity = string.Empty;
    public string LastFailure = string.Empty;

    public CMUUtilityProgram Program = new();
    public bool ProgramRunning;
    public bool ProgramPaused;
    public int StepIndex;
    public TimeSpan StepStarted;
    public TimeSpan StepDeadline;
    public TimeSpan WaitUntil;
    public TimeSpan RepeatAt;
    public int PathAttempts;
    public int Waypoint;
    public int StepPhase;

    public EntityCoordinates? Destination;
    public EntityUid? WorkTarget;
    public EntityUid? FetchedItem;
    public EntityUid? ReportSubject;

    public CMUUtilityTrigger? PendingTrigger;
    public EntityUid? PendingSubject;
    public CMUUtilityTrigger? PendingTriggerAfter;
    public EntityUid? PendingSubjectAfter;
    public EntityUid? LastPulledEntity;
    public EntityUid? LastAttacker;
    public bool DamagePending;
    public bool ForcedReturn;
    public bool StopAfterReturn;
    public TimeSpan ForcedReturnUntil;

    // Script combat runtime. These values live only on the server and are
    // cleared when the program stops or a new program starts.
    public EntityUid? CombatTarget;
    public EntityCoordinates? CombatCoordinates;
    public EntityUid? CombatWeapon;
    public bool WeaponsHold;
    public bool CombatAbort;
    public TimeSpan CombatUntil;

    public TimeSpan NextThink;
    public TimeSpan NextSense;
    public TimeSpan NextPullCheck;
    public TimeSpan NextChat;
    public TimeSpan NextRetry;
    public TimeSpan WorkStarted;
    public TimeSpan LastProgress;
    public int ScanCandidates;
    public readonly Dictionary<EntityUid, TimeSpan> NextReportBySubject = new();

    public long ProgramsCompleted;
    public long StepsCompleted;
    public long ReportsSent;
    public long ProgramFailures;
}

/// <summary>A local stair approach followed by a physical crossing into the next floor.</summary>
[RegisterComponent]
public sealed partial class CMUUtilityDroneTraversalComponent : Component
{
    public CMUUtilityDroneStairRoute? Route;
    public bool Crossing;
    public TimeSpan ProgressDeadline;
    public float LastProgressDistance = float.MaxValue;
    public readonly Dictionary<EntityUid, TimeSpan> FailedStairs = new();
}

public readonly record struct CMUUtilityDroneStairRoute(
    EntityUid Stair,
    EntityUid FromMap,
    EntityUid ToMap,
    Vector2 Approach,
    Vector2 Exit);

/// <summary>
/// A physical task marker item linked to one console. Programs may only refer
/// to markers registered with the console that printed their directive board.
/// </summary>
[RegisterComponent]
public sealed partial class CMUUtilityTaskMarkerComponent : Component
{
    [DataField]
    public EntityUid? Console;
}

/// <summary>
/// A physical directive board printed by a utility drone console. The board
/// carries a validated program and is installed into one drone's board slot.
/// </summary>
[RegisterComponent]
public sealed partial class CMUUtilityDroneProgramBoardComponent : Component
{
    [DataField]
    public CMUUtilityProgram Program = new();

    [DataField]
    public EntityUid? Console;

    [DataField]
    public string Script = string.Empty;

    [DataField]
    public List<EntityUid> ScriptMarkers = new();
}
