using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Robotics.QueueNetwork;

[Serializable, NetSerializable]
public enum CMUQueueNetworkConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public enum CMUQueueStatusKind : byte
{
    Neutral,
    Good,
    Bad,
}

[Serializable, NetSerializable]
public sealed class CMUQueueNodeInfo
{
    public string Id { get; }
    public string Name { get; }
    public string Suffix { get; }
    public double ServiceRate { get; }
    public double BaseServiceRate { get; }
    public double MaxServiceRate { get; }
    public double ExternalRate { get; }
    public double Queue { get; }
    public int Invested { get; }
    public double CostPerMu { get; }
    public int UpgradeCost { get; }
    public int Column { get; }
    public int Row { get; }
    public bool Analyzed { get; }
    public double Lambda { get; }
    public double Rho { get; }
    public double L { get; }
    public double Lq { get; }
    public double W { get; }
    public double Wq { get; }
    public bool Stable { get; }

    public CMUQueueNodeInfo(
        string id,
        string name,
        string suffix,
        double serviceRate,
        double baseServiceRate,
        double maxServiceRate,
        double externalRate,
        double queue,
        int invested,
        double costPerMu,
        int upgradeCost,
        int column,
        int row,
        bool analyzed,
        double lambda,
        double rho,
        double l,
        double lq,
        double w,
        double wq,
        bool stable)
    {
        Id = id;
        Name = name;
        Suffix = suffix;
        ServiceRate = serviceRate;
        BaseServiceRate = baseServiceRate;
        MaxServiceRate = maxServiceRate;
        ExternalRate = externalRate;
        Queue = queue;
        Invested = invested;
        CostPerMu = costPerMu;
        UpgradeCost = upgradeCost;
        Column = column;
        Row = row;
        Analyzed = analyzed;
        Lambda = lambda;
        Rho = rho;
        L = l;
        Lq = lq;
        W = w;
        Wq = wq;
        Stable = stable;
    }
}

[Serializable, NetSerializable]
public sealed class CMUQueueRouteInfo
{
    public string From { get; }
    public string To { get; }
    public double Probability { get; }

    public CMUQueueRouteInfo(string from, string to, double probability)
    {
        From = from;
        To = to;
        Probability = probability;
    }
}

[Serializable, NetSerializable]
public sealed class CMUQueueBypassInfo
{
    public string Id { get; }
    public string From { get; }
    public string To { get; }
    public double CurrentPercent { get; }
    public double MaxPercent { get; }
    public double StepPercent { get; }
    public int NextCost { get; }
    public bool CanAfford { get; }
    public bool Maxed { get; }

    public CMUQueueBypassInfo(
        string id,
        string from,
        string to,
        double currentPercent,
        double maxPercent,
        double stepPercent,
        int nextCost,
        bool canAfford,
        bool maxed)
    {
        Id = id;
        From = from;
        To = to;
        CurrentPercent = currentPercent;
        MaxPercent = maxPercent;
        StepPercent = stepPercent;
        NextCost = nextCost;
        CanAfford = canAfford;
        Maxed = maxed;
    }
}

[Serializable, NetSerializable]
public sealed class CMUQueueHistoryInfo
{
    public float Minute { get; }
    public List<float> Queues { get; }

    public CMUQueueHistoryInfo(float minute, List<float> queues)
    {
        Minute = minute;
        Queues = queues;
    }
}

[Serializable, NetSerializable]
public sealed class CMUQueueNetworkConsoleState : BoundUserInterfaceState
{
    public bool Active { get; }
    public bool Completed { get; }
    public string Status { get; }
    public CMUQueueStatusKind StatusKind { get; }
    public string NetworkName { get; }
    public string NetworkDescription { get; }
    public int Budget { get; }
    public int TimeLeft { get; }
    public int InitialTime { get; }
    public int Reward { get; }
    public int Elapsed { get; }
    public int AnalysisCost { get; }
    public int ProjectionCost { get; }
    public int ProjectionMinutes { get; }
    public List<CMUQueueNodeInfo> Nodes { get; }
    public List<CMUQueueRouteInfo> Routes { get; }
    public List<CMUQueueBypassInfo> Bypasses { get; }
    public List<CMUQueueHistoryInfo> History { get; }

    public CMUQueueNetworkConsoleState(
        bool active,
        bool completed,
        string status,
        CMUQueueStatusKind statusKind,
        string networkName,
        string networkDescription,
        int budget,
        int timeLeft,
        int initialTime,
        int reward,
        int elapsed,
        int analysisCost,
        int projectionCost,
        int projectionMinutes,
        List<CMUQueueNodeInfo> nodes,
        List<CMUQueueRouteInfo> routes,
        List<CMUQueueBypassInfo> bypasses,
        List<CMUQueueHistoryInfo> history)
    {
        Active = active;
        Completed = completed;
        Status = status;
        StatusKind = statusKind;
        NetworkName = networkName;
        NetworkDescription = networkDescription;
        Budget = budget;
        TimeLeft = timeLeft;
        InitialTime = initialTime;
        Reward = reward;
        Elapsed = elapsed;
        AnalysisCost = analysisCost;
        ProjectionCost = projectionCost;
        ProjectionMinutes = projectionMinutes;
        Nodes = nodes;
        Routes = routes;
        Bypasses = bypasses;
        History = history;
    }
}

[Serializable, NetSerializable]
public sealed class CMUQueueAnalyzeNodeMessage(string nodeId) : BoundUserInterfaceMessage
{
    public string NodeId { get; } = nodeId;
}

[Serializable, NetSerializable]
public sealed class CMUQueueSetServiceRateMessage(string nodeId, double targetServiceRate)
    : BoundUserInterfaceMessage
{
    public string NodeId { get; } = nodeId;
    public double TargetServiceRate { get; } = targetServiceRate;
}

[Serializable, NetSerializable]
public sealed class CMUQueueRerouteMessage(string bypassId) : BoundUserInterfaceMessage
{
    public string BypassId { get; } = bypassId;
}

[Serializable, NetSerializable]
public sealed class CMUQueueProjectMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMUQueueSubmitMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMUQueueNewContractMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMUQueueResetInterventionsMessage : BoundUserInterfaceMessage;
