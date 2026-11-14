using Content.Shared.CMU14.Configuration;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.DroneOperator;

[Serializable, NetSerializable]
public enum CMUDroneConfigurationUi : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum CMUUtilityDroneConfigAction : byte
{
    SetProgrammableMode,
    SetBoardPortLocked,
    RestartProgram,
    PauseProgram,
    ResumeProgram,
    StopProgram,
    EjectProgramBoard,
}

[Serializable, NetSerializable]
public sealed class CMUDroneConfigurationBuiState : BoundUserInterfaceState
{
    public List<CMUConfigurationOption> Factions { get; }
    public string CurrentFaction { get; }
    public List<CMUConfigurationOption> Channels { get; }
    public HashSet<string> SelectedChannels { get; }
    public List<CMUConfigurationOption> Access { get; }
    public HashSet<string> SelectedAccess { get; }
    public bool HasIdCard { get; }
    public bool UtilityDrone { get; }
    public bool ProgrammableMode { get; }
    public bool BoardPortLocked { get; }
    public bool BoardInstalled { get; }
    public bool ProgramRunning { get; }
    public bool ProgramPaused { get; }
    public int Step { get; }
    public int StepCount { get; }
    public string Activity { get; }

    public CMUDroneConfigurationBuiState(
        List<CMUConfigurationOption> factions,
        string currentFaction,
        List<CMUConfigurationOption> channels,
        HashSet<string> selectedChannels,
        List<CMUConfigurationOption> access,
        HashSet<string> selectedAccess,
        bool hasIdCard,
        bool utilityDrone = false,
        bool programmableMode = false,
        bool boardPortLocked = true,
        bool boardInstalled = false,
        bool programRunning = false,
        bool programPaused = false,
        int step = 0,
        int stepCount = 0,
        string activity = "")
    {
        Factions = factions;
        CurrentFaction = currentFaction;
        Channels = channels;
        SelectedChannels = selectedChannels;
        Access = access;
        SelectedAccess = selectedAccess;
        HasIdCard = hasIdCard;
        UtilityDrone = utilityDrone;
        ProgrammableMode = programmableMode;
        BoardPortLocked = boardPortLocked;
        BoardInstalled = boardInstalled;
        ProgramRunning = programRunning;
        ProgramPaused = programPaused;
        Step = step;
        StepCount = stepCount;
        Activity = activity;
    }
}

[Serializable, NetSerializable]
public sealed class CMUDroneConfigurationApplyMsg : BoundUserInterfaceMessage
{
    public string Faction { get; }
    public HashSet<string> Channels { get; }
    public HashSet<string> Access { get; }

    public CMUDroneConfigurationApplyMsg(string faction, HashSet<string> channels, HashSet<string> access)
    {
        Faction = faction;
        Channels = channels;
        Access = access;
    }
}

[Serializable, NetSerializable]
public sealed class CMUUtilityDroneConfigMsg : BoundUserInterfaceMessage
{
    public CMUUtilityDroneConfigAction Action { get; }
    public bool Enabled { get; }

    public CMUUtilityDroneConfigMsg(CMUUtilityDroneConfigAction action, bool enabled = false)
    {
        Action = action;
        Enabled = enabled;
    }
}
