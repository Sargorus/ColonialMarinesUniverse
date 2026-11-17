using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Hands.Components;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private ResearchKnowledgeSubmitBuiState BuildChemistryState(EntityUid user, string faction)
    {
        var locked = _research.IsResearchBlocked(faction);
        var known = _research.GetResearch(faction).KnownFacts;
        var available = GetAvailableFacts(user);
        var choices = new List<ResearchKnowledgeChoice>();
        foreach (var (factId, source) in available)
        {
            if (!_facts.TryGetValue(factId, out var fact))
                continue;
            var witness = IsWitnessFact(fact);
            var witnessCap = witness && _witnessClaims.GetValueOrDefault(factId) >= fact.MaxSubmissions;
            var playerClaim = witness && _playerClaims.Contains((factId, user));
            var witnessNotPersonal = witness && !HasWitnessed(user, factId);
            var alreadyKnown = !witness && known.Contains(factId);
            var disabled = locked || alreadyKnown || witnessCap || playerClaim || witnessNotPersonal;
            var reason = locked
                ? Loc.GetString("cmu-research-documents-locked")
                : alreadyKnown
                    ? Loc.GetString("cmu-research-documents-already-known")
                    : witnessCap
                        ? Loc.GetString("cmu-research-documents-witness-cap")
                        : playerClaim
                            ? Loc.GetString("cmu-research-documents-player-claimed")
                            : witnessNotPersonal
                                ? Loc.GetString("cmu-research-documents-witness-not-personal")
                                : null;
            var cash = !witness && fact.SourceFaction == faction ? fact.Cash : 0;
            var reward = disabled
                ? reason ?? string.Empty
                : witness
                    ? FormatReward(0, fact.SampleData, 0)
                    : FormatReward(GetFactCredits(fact, faction), fact.SampleData, cash);
            choices.Add(new ResearchKnowledgeChoice(
                fact.Id,
                fact.Title,
                CategoryName(fact),
                source,
                reward,
                disabled,
                reason));
        }
        return new ResearchKnowledgeSubmitBuiState(
            locked,
            Loc.GetString("cmu-research-documents-submit-title", ("faction", faction)),
            choices);
    }

    private Dictionary<string, string> GetAvailableFacts(EntityUid user)
    {
        var available = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TryComp<ResearchKnowledgeComponent>(user, out var knowledge))
        {
            foreach (var factId in knowledge.Facts)
            {
                if (_facts.ContainsKey(factId))
                    available[factId] = Loc.GetString("cmu-research-documents-source-personal");
            }
        }
        if (TryComp<HandsComponent>(user, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((user, hands)))
            {
                if (!TryComp<ResearchDocumentComponent>(held, out var document))
                    continue;
                EnsureScannerReportFacts(held, document);
                var source = Loc.GetString("cmu-research-documents-source-paper", ("paper", Name(held)));
                foreach (var factId in document.FactIds)
                {
                    if (_facts.ContainsKey(factId))
                        available.TryAdd(factId, source);
                }
            }
        }
        return available;
    }
}
