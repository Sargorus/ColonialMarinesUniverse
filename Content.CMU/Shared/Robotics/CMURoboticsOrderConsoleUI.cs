using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Shared.GameObjects;
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
public sealed class CMURoboticsCartridgeInfo
{
    public EntProtoId Prototype { get; }
    public string Description { get; }
    public int Cost { get; }

    public CMURoboticsCartridgeInfo(EntProtoId prototype, string description, int cost)
    {
        Prototype = prototype;
        Description = description;
        Cost = cost;
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
    public int CooldownSeconds { get; }
    public CMUQueueNetworkConsoleState? Queue { get; }
    public List<CMURoboticsCartridgeInfo> Cartridges { get; }
    public int RoboticsPoints { get; }
    public string CartridgeStatus { get; }

    public CMURoboticsOrderConsoleState(
        List<CMURoboticsOrderInfo> available,
        CMURoboticsOrderInfo? active,
        string status,
        int lastPenalty,
        int lastMissingElements,
        int cooldownSeconds = 0,
        CMUQueueNetworkConsoleState? queue = null,
        List<CMURoboticsCartridgeInfo>? cartridges = null,
        int roboticsPoints = 0,
        string cartridgeStatus = "")
    {
        Available = available;
        Active = active;
        Status = status;
        LastPenalty = lastPenalty;
        LastMissingElements = lastMissingElements;
        CooldownSeconds = cooldownSeconds;
        Queue = queue;
        Cartridges = cartridges ?? new List<CMURoboticsCartridgeInfo>();
        RoboticsPoints = roboticsPoints;
        CartridgeStatus = cartridgeStatus;
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

[Serializable, NetSerializable]
public sealed class CMURoboticsPurchaseCartridgeMessage(string prototype) : BoundUserInterfaceMessage
{
    public string Prototype { get; } = prototype;
}

/// <summary>
/// Raised on a robotics terminal after the embedded queue-network state
/// changes, so the order system can push a fresh combined UI state.
/// </summary>
[ByRefEvent]
public readonly record struct CMURoboticsTerminalQueueUpdatedEvent;
