using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Chemistry.Research;

/// <summary>
/// Facts a player has personally memorized. The server owns this component; clients receive the
/// current list through the research document UI states.
/// </summary>
[RegisterComponent]
public sealed partial class ResearchKnowledgeComponent : Component
{
    [DataField]
    public HashSet<string> Facts = [];

    /// <summary>
    /// Witness facts this player personally observed. Reading another person's report adds the fact to
    /// <see cref="Facts"/> but not here, so it cannot be submitted to an intel or chemistry terminal.
    /// </summary>
    [DataField]
    public HashSet<string> WitnessedFacts = [];
}

/// <summary>
/// A paper that carries research facts. Scanner reports only become player knowledge through their
/// "Remember" verb; documents written by players are read with a short do-after.
/// </summary>
[RegisterComponent]
public sealed partial class ResearchDocumentComponent : Component
{
    [DataField]
    public List<string> FactIds = [];

    [DataField]
    public bool ScannerReport;

    /// <summary>Faction that produced a scanner report; used to reconstruct facts from older reports.</summary>
    [DataField]
    public string? SourceFaction;

    [DataField]
    public string Author = string.Empty;

    [DataField]
    public string Title = string.Empty;

    [DataField]
    public ResearchDocumentType Type = ResearchDocumentType.Report;

    [DataField]
    public TimeSpan CreatedAt;

    [DataField]
    public HashSet<string> SubmittedFactions = [];
}

[Serializable, NetSerializable]
public enum ResearchDocumentType
{
    Report,
    Record,
    Instruction,
    Memo,
}
