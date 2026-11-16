using System;
using System.Collections.Generic;
using System.Text;
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
        var id = $"contract:{proto.ID}";
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
        var id = $"property:{property.ID}";
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
        var id = $"botany:{proto.ID}";
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
            MaxSubmissions = 3,
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
        bool firstCompletion,
        IReadOnlyCollection<string> newPropertyIds)
    {
        var factIds = new List<string>();
        if (firstCompletion
            && report.Completed
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
        if (firstCompletion && report.Completed && chem.Class == ReagentClass.Hydro)
            factIds.Add(RecordBotanyFact(faction, chem).Id);
        if (factIds.Count == 0)
            return 0;

        var document = EnsureComp<ResearchDocumentComponent>(paper);
        document.FactIds = factIds;
        document.ScannerReport = true;
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

    public string BuildDocumentText(
        string title,
        string author,
        ResearchDocumentType type,
        IReadOnlyCollection<string> factIds)
    {
        var text = new StringBuilder();
        text.AppendLine(Loc.GetString("cmu-research-document-header"));
        text.AppendLine(Loc.GetString("cmu-research-document-title", ("title", FormattedMessage.EscapeText(title))));
        text.AppendLine(Loc.GetString("cmu-research-document-type", ("type", DocumentTypeName(type))));
        text.AppendLine(Loc.GetString("cmu-research-document-author", ("author", FormattedMessage.EscapeText(author))));
        text.AppendLine(Loc.GetString("cmu-research-document-time", ("time", _timing.CurTime.ToString(@"hh\:mm"))));
        text.AppendLine();
        text.AppendLine(Loc.GetString("cmu-research-document-findings"));
        foreach (var factId in factIds)
        {
            if (!_facts.TryGetValue(factId, out var fact))
                continue;
            text.AppendLine(Loc.GetString(
                "cmu-research-document-entry",
                ("title", fact.Title),
                ("description", fact.Description)));
        }
        text.AppendLine();
        text.Append(Loc.GetString("cmu-research-document-footer"));
        return text.ToString();
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
