using System;
using System.Collections.Generic;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Chemistry.Research;

[Serializable, NetSerializable]
public enum ResearchKnowledgeSubmitUI
{
    Key,
}

[Serializable, NetSerializable]
public enum ResearchDocumentWriteUI
{
    Key,
}

/// <summary>
/// Server-built list of facts that can be submitted at a chemistry or intel console. The client only
/// displays the already resolved title, source, reward and disabled reason.
/// </summary>
[Serializable, NetSerializable]
public sealed class ResearchKnowledgeSubmitBuiState(
    bool locked,
    string title,
    List<ResearchKnowledgeChoice> choices) : BoundUserInterfaceState
{
    public readonly bool Locked = locked;
    public readonly string Title = title;
    public readonly List<ResearchKnowledgeChoice> Choices = choices;
}

[Serializable, NetSerializable]
public sealed class ResearchDocumentWriteBuiState(List<ResearchKnowledgeChoice> facts) : BoundUserInterfaceState
{
    public readonly List<ResearchKnowledgeChoice> Facts = facts;
}

[Serializable, NetSerializable]
public readonly record struct ResearchKnowledgeChoice(
    string FactId,
    string Title,
    string Category,
    string Source,
    string Reward,
    bool Disabled,
    string? DisabledReason);

[Serializable, NetSerializable]
public sealed class ResearchKnowledgeSubmitMessage(List<string> factIds) : BoundUserInterfaceMessage
{
    public readonly List<string> FactIds = factIds;
}

[Serializable, NetSerializable]
public sealed class ResearchDocumentWriteMessage(
    string title,
    ResearchDocumentType type,
    List<string> factIds) : BoundUserInterfaceMessage
{
    public readonly string Title = title;
    public readonly ResearchDocumentType Type = type;
    public readonly List<string> FactIds = factIds;
}
