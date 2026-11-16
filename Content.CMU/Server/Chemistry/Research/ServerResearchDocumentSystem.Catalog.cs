using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Research;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private void InitializeCatalog()
    {
        SubscribeLocalEvent<ResearchKnowledgeCatalogEvent>(OnCatalogRequest);
    }

    private void OnCatalogRequest(ResearchKnowledgeCatalogEvent args)
    {
        args.Entries.Clear();
        var research = _research.GetResearch(args.Faction);
        var rows = new List<(ResearchFact Fact, ResearchKnowledgeCatalogEntry Entry)>();
        foreach (var fact in _facts.Values)
        {
            if (!IsCatalogVisible(fact, args.Faction, research))
                continue;

            rows.Add((fact, new ResearchKnowledgeCatalogEntry(
                fact.Title,
                CategoryName(fact),
                BuildCatalogReward(fact, args.Faction),
                BuildCatalogDetail(fact, args.Faction, research))));
        }
        rows.Sort((a, b) =>
        {
            var rank = CatalogRank(a.Fact.Kind).CompareTo(CatalogRank(b.Fact.Kind));
            return rank != 0
                ? rank
                : string.Compare(a.Fact.Title, b.Fact.Title, StringComparison.CurrentCulture);
        });
        foreach (var (_, entry) in rows)
            args.Entries.Add(entry);
    }

    private bool IsCatalogVisible(
        ResearchFact fact,
        string faction,
        ServerResearchDataTerminalSystem.FactionResearch research)
    {
        return fact.SourceFaction is null
               || string.Equals(fact.SourceFaction, faction, StringComparison.OrdinalIgnoreCase)
               || research.KnownFacts.Contains(fact.Id);
    }

    private string BuildCatalogReward(ResearchFact fact, string faction)
    {
        var parts = new List<string>();
        if (fact.Credits != 0)
            parts.Add(Loc.GetString("cmu-research-documents-reward-credits", ("credits", fact.Credits)));
        if (fact.SampleData != 0)
            parts.Add(Loc.GetString("cmu-research-documents-reward-data", ("data", fact.SampleData)));

        var cash = 0;
        if (fact.SourceFaction is not null
            && string.Equals(fact.SourceFaction, faction, StringComparison.OrdinalIgnoreCase))
        {
            cash = fact.Cash;
        }
        if (cash != 0)
            parts.Add(Loc.GetString("cmu-research-documents-reward-cash", ("cash", cash)));
        if (fact.IntelPoints > 0 && IsResearchIntelTeam(faction))
        {
            parts.Add(Loc.GetString(
                "cmu-research-documents-intel-reward",
                ("points", fact.IntelPoints.ToString("0.##"))));
        }
        return parts.Count == 0
            ? Loc.GetString("cmu-research-documents-reward-none")
            : string.Join(", ", parts);
    }

    private string BuildCatalogDetail(
        ResearchFact fact,
        string faction,
        ServerResearchDataTerminalSystem.FactionResearch research)
    {
        if (IsWitnessFact(fact))
        {
            return Loc.GetString(
                "cmu-research-catalog-detail-witness",
                ("accepted", _witnessClaims.GetValueOrDefault(fact.Id)),
                ("max", fact.MaxSubmissions));
        }
        if (!string.Equals(fact.SourceFaction, faction, StringComparison.OrdinalIgnoreCase))
            return Loc.GetString("cmu-research-catalog-detail-foreign");

        if (fact.Kind == ResearchFactKind.Contract
            && fact.ReagentId is { } reagentId
            && research.RewardedChemicals.Contains(reagentId))
        {
            return Loc.GetString("cmu-research-catalog-detail-cash-paid");
        }

        return fact.Kind switch
        {
            ResearchFactKind.Contract => Loc.GetString("cmu-research-catalog-detail-contract"),
            ResearchFactKind.Property => Loc.GetString("cmu-research-catalog-detail-property"),
            ResearchFactKind.Botany => Loc.GetString("cmu-research-catalog-detail-botany"),
            _ => Loc.GetString("cmu-research-catalog-detail-own"),
        };
    }

    private static int CatalogRank(ResearchFactKind kind)
    {
        return kind switch
        {
            ResearchFactKind.Contract => 0,
            ResearchFactKind.Property => 1,
            ResearchFactKind.Botany => 2,
            ResearchFactKind.XenoSighting => 3,
            ResearchFactKind.WendigoSighting => 4,
            ResearchFactKind.Infection => 5,
            ResearchFactKind.Burst => 6,
            _ => 7,
        };
    }
}
