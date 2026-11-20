using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Robotics;

[Serializable, NetSerializable]
public enum CMURoboticsOrderConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public enum CMURoboticsOrderType : byte
{
    Build,
    Repair,
}

[Serializable, NetSerializable]
public sealed class CMURoboticsOrderInfo
{
    public int Id { get; }
    public CMURoboticsOrderType Type { get; }
    public string TargetNameLocId { get; }
    public EntProtoId? RequiredModule { get; }
    public int Reward { get; }
    public int Amount { get; }
    public int SecondsRemaining { get; }

    public CMURoboticsOrderInfo(
        int id,
        CMURoboticsOrderType type,
        string targetNameLocId,
        EntProtoId? requiredModule,
        int reward,
        int amount,
        int secondsRemaining)
    {
        Id = id;
        Type = type;
        TargetNameLocId = targetNameLocId;
        RequiredModule = requiredModule;
        Reward = reward;
        Amount = amount;
        SecondsRemaining = secondsRemaining;
    }
}

[Serializable, NetSerializable]
public sealed class CMURoboticsOrderConsoleState : BoundUserInterfaceState
{
    public List<CMURoboticsOrderInfo> Available { get; }
    public CMURoboticsOrderInfo? Active { get; }
    public string Status { get; }
    public int LastPenalty { get; }
    public int LastMissingElements { get; }

    public CMURoboticsOrderConsoleState(
        List<CMURoboticsOrderInfo> available,
        CMURoboticsOrderInfo? active,
        string status,
        int lastPenalty,
        int lastMissingElements)
    {
        Available = available;
        Active = active;
        Status = status;
        LastPenalty = lastPenalty;
        LastMissingElements = lastMissingElements;
    }
}

[Serializable, NetSerializable]
public sealed class CMURoboticsAcceptOrderMessage(int id) : BoundUserInterfaceMessage
{
    public int Id { get; } = id;
}

[Serializable, NetSerializable]
public sealed class CMURoboticsCancelOrderMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMURoboticsPrintSlipMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMURoboticsRefreshOrdersMessage : BoundUserInterfaceMessage;
