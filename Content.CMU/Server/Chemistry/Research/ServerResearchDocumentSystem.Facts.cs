using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Fax.Components;
using Content.Shared.Paper;
using Robust.Shared.Utility;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    public ResearchFact RecordContractFact(string faction, ReagentPrototype proto)
    {
        var id = $"contract:{faction}:{proto.ID}";
        if (_facts.TryGetValue(id, out var existing))
            return existing;
        var fact = new ResearchFact
        {
            Id = id,
            Kind = ResearchFactKind.Contract,
            SourceFaction = faction,
            ReagentId = proto.ID,
            Credits = proto.Reward,
            SampleData = ServerResearchDataTerminalSystem.SampleDataForCompleted(proto),
            Cash = (int)MathF.Round(_research.ResearchCashRewardMult * proto.Reward),
            IntelPoints = 0.2f,
            Title = Loc.GetString("cmu-research-fact-contract-title", ("name", proto.LocalizedName)),
            Description = Loc.GetString("cmu-research-fact-contract-desc", ("name", proto.LocalizedName)),
        };
        _facts[id] = fact;
        return fact;
    }

    public ResearchFact RecordPropertyFact(string faction, ReagentPropertyPrototype property)
    {
        var id = $"property:{faction}:{property.ID}";
        if (_facts.TryGetValue(id, out var existing))
            return existing;
        var fact = new ResearchFact
        {
            Id = id,
            Kind = ResearchFactKind.Property,
            SourceFaction = faction,
            SampleData = 1,
            IntelPoints = 0.1f,
            Title = Loc.GetString("cmu-research-fact-property-title", ("name", property.LocalizedName)),
            Description = Loc.GetString("cmu-research-fact-property-desc", ("name", property.LocalizedName)),
        };
        _facts[id] = fact;
        return fact;
    }

    public ResearchFact RecordBotanyFact(string faction, ReagentPrototype proto)
    {
        var id = $"botany:{faction}:{proto.ID}";
        if (_facts.TryGetValue(id, out var existing))
            return existing;
        var fact = new ResearchFact
        {
            Id = id,
            Kind = ResearchFactKind.Botany,
            SourceFaction = faction,
            ReagentId = proto.ID,
            SampleData = 1,
            IntelPoints = 0.1f,
            Title = Loc.GetString("cmu-research-fact-botany-title", ("name", proto.LocalizedName)),
            Description = Loc.GetString("cmu-research-fact-botany-desc", ("name", proto.LocalizedName)),
        };
        _facts[id] = fact;
        return fact;
    }

    public ResearchFact RecordWitnessFact(
        string factId,
        ResearchFactKind kind,
        string title,
        string description,
        int sampleData,
        float intelPoints)
    {
        if (_facts.TryGetValue(factId, out var existing))
            return existing;
        var fact = new ResearchFact
        {
            Id = factId,
            Kind = kind,
            SampleData = sampleData,
            IntelPoints = intelPoints,
            MaxSubmissions = WitnessSubmissionLimit,
            Title = title,
            Description = description,
        };
        _facts[factId] = fact;
        return fact;
    }

    /// <summary>
    /// Adds the facts an XRF report can carry. A report only becomes a research document when at least one
    /// fact was recorded; otherwise it remains an ordinary analysis printout.
    /// </summary>
    public int AttachScannerReport(
        EntityUid paper,
        string faction,
        ReagentPrototype chem,
        ResearchReportData report,
        IReadOnlyCollection<string> newPropertyIds)
    {
        var factIds = new List<string>();
        if (report.Completed
            && chem.Class != ReagentClass.Hydro
            && (chem.Class >= ReagentClass.Special || chem.Generated))
        {
            factIds.Add(RecordContractFact(faction, chem).Id);
        }
        if (report.Completed)
        {
            foreach (var propertyId in newPropertyIds)
            {
                if (_prototypes.TryIndex<ReagentPropertyPrototype>(propertyId, out var property))
                    factIds.Add(RecordPropertyFact(faction, property).Id);
            }
        }
        if (report.Completed && chem.Class == ReagentClass.Hydro)
            factIds.Add(RecordBotanyFact(faction, chem).Id);
        if (factIds.Count == 0)
            return 0;

        var document = EnsureComp<ResearchDocumentComponent>(paper);
        document.FactIds = factIds;
        document.ScannerReport = true;
        document.SourceFaction = faction;
        document.Author = Loc.GetString("cmu-research-document-xrf-author");
        document.Title = Name(paper);
        document.Type = ResearchDocumentType.Report;
        document.CreatedAt = _timing.CurTime;
        RemComp<FaxableObjectComponent>(paper);
        if (TryComp<PaperComponent>(paper, out var paperComp))
        {
            _paper.SetContent(
                (paper, paperComp),
                paperComp.Content + '\n' + Loc.GetString("cmu-paper-xrf-memory-hint"));
        }
        return factIds.Count;
    }

    /// <summary>
    /// Rebuilds a scanner report's facts from its stored reagent report when an older print or a copied
    /// document has no fact list. Current reports already carry the list, so this is a compatibility path.
    /// </summary>
    public int EnsureScannerReportFacts(EntityUid paper, ResearchDocumentComponent document)
    {
        var allRegistered = document.FactIds.Count > 0;
        foreach (var factId in document.FactIds)
        {
            if (_facts.ContainsKey(factId))
                continue;
            allRegistered = false;
            break;
        }
        if (allRegistered)
            return document.FactIds.Count;
        if (!document.ScannerReport
            || string.IsNullOrEmpty(document.SourceFaction)
            || !TryComp<ResearchReportComponent>(paper, out var report)
            || report.Data is not { } data
            || !_reagent.TryIndex(data.ID, out var chem))
        {
            return 0;
        }

        var factIds = new List<string>();
        if (chem.Class != ReagentClass.Hydro
            && (chem.Class >= ReagentClass.Special || chem.Generated))
        {
            factIds.Add(RecordContractFact(document.SourceFaction, chem).Id);
        }
        foreach (var propertyId in data.Effects.Keys)
        {
            if (_prototypes.TryIndex<ReagentPropertyPrototype>(propertyId, out var property))
                factIds.Add(RecordPropertyFact(document.SourceFaction, property).Id);
        }
        if (chem.Class == ReagentClass.Hydro)
            factIds.Add(RecordBotanyFact(document.SourceFaction, chem).Id);
        document.FactIds = factIds;
        return factIds.Count;
    }

    public string BuildDocumentDescription(
        string title,
        string author,
        ResearchDocumentType type,
        IReadOnlyCollection<string> factIds)
    {
        var findings = new List<string>();
        foreach (var factId in factIds)
        {
            if (_facts.TryGetValue(factId, out var fact))
            {
                findings.Add(
                    $"{FormattedMessage.EscapeText(fact.Title)}: {FormattedMessage.EscapeText(fact.Description)}");
            }
        }
        return Loc.GetString(
            "cmu-research-document-description",
            ("title", FormattedMessage.EscapeText(title)),
            ("type", DocumentTypeName(type)),
            ("author", FormattedMessage.EscapeText(author)),
            ("finding", string.Join("; ", findings)));
    }

    private string CategoryName(ResearchFact fact)
        => Loc.GetString(fact.Kind switch
        {
            ResearchFactKind.Contract => "cmu-research-documents-category-contract",
            ResearchFactKind.Property => "cmu-research-documents-category-property",
            ResearchFactKind.Botany => "cmu-research-documents-category-botany",
            ResearchFactKind.WendigoSighting => "cmu-research-documents-category-wendigo",
            ResearchFactKind.Infection => "cmu-research-documents-category-infection",
            ResearchFactKind.Burst => "cmu-research-documents-category-burst",
            _ => "cmu-research-documents-category-sighting",
        });
}
