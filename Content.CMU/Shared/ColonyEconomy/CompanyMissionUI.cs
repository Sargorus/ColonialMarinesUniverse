using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.ColonyEconomy;

/// <summary>Which science branch the liaison has made the complex's main direction.</summary>
[Serializable, NetSerializable]
public enum CorporateResearchDirection : byte
{
    None,
    Robotics,
    Bioengineering,
}

[Serializable, NetSerializable]
public enum CompanyMissionStatus : byte
{
    Available,
    Active,
    Completed,
    Failed,
}

/// <summary>
/// One company mission as shown by the liaison console. The server localizes the name, description and
/// objective line before sending it, so the client only renders text and progress.
/// </summary>
[Serializable, NetSerializable]
public sealed class CompanyMissionInfo
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Objective { get; }
    public int Reward { get; }
    public int Progress { get; }
    public int Target { get; }
    public int DeadlineMinute { get; }
    public int SecondsRemaining { get; }
    public CompanyMissionStatus Status { get; }

    public CompanyMissionInfo(
        string id,
        string name,
        string description,
        string objective,
        int reward,
        int progress,
        int target,
        int deadlineMinute,
        int secondsRemaining,
        CompanyMissionStatus status)
    {
        Id = id;
        Name = name;
        Description = description;
        Objective = objective;
        Reward = reward;
        Progress = progress;
        Target = target;
        DeadlineMinute = deadlineMinute;
        SecondsRemaining = secondsRemaining;
        Status = status;
    }
}

[Serializable, NetSerializable]
public sealed class CorporateConsoleAcceptMissionBuiMsg(string missionId) : BoundUserInterfaceMessage
{
    public string MissionId { get; } = missionId;
}

[Serializable, NetSerializable]
public sealed class CorporateConsoleSetDirectionBuiMsg(CorporateResearchDirection direction) : BoundUserInterfaceMessage
{
    public CorporateResearchDirection Direction { get; } = direction;
}

[Serializable, NetSerializable]
public sealed class CorporateConsoleCallPmcBuiMsg : BoundUserInterfaceMessage;
