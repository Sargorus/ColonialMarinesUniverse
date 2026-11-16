using System;
using System.Collections.Generic;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Database;
using Content.Shared.Hands.Components;
using Content.Shared.Verbs;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private void InitializeSubmission()
    {
        SubscribeLocalEvent<ResearchDataTerminalComponent, GetVerbsEvent<AlternativeVerb>>(OnTerminalVerbs);
        Subs.BuiEvents<ResearchDataTerminalComponent>(ResearchKnowledgeSubmitUI.Key,
            subs =>
            {
                subs.Event<ResearchKnowledgeSubmitMessage>(OnSubmitMessage);
            });
        Subs.BuiEvents<ResearchDataTerminalComponent>(ResearchDataTerminalUI.Key,
            subs =>
            {
                subs.Event<ResearchDataTerminalOpenKnowledgeBuiMsg>(OnOpenKnowledgeMessage);
            });
    }

    private void OnOpenKnowledgeMessage(
        Entity<ResearchDataTerminalComponent> ent,
        ref ResearchDataTerminalOpenKnowledgeBuiMsg args)
    {
        _ui.CloseUi(ent.Owner, ResearchDataTerminalUI.Key, args.Actor);
        OpenSubmitUi(args.Actor, ent.Owner);
    }

    private void OnTerminalVerbs(Entity<ResearchDataTerminalComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || HasComp<XenoComponent>(args.User))
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-research-documents-submit-verb"),
            Act = () => OpenSubmitUi(user, ent.Owner),
        });
    }

    private void OpenSubmitUi(EntityUid user, EntityUid terminal)
    {
        if (!TryComp<ResearchDataTerminalComponent>(terminal, out var terminalComp))
            return;
        var state = BuildChemistryState(user, terminalComp.Faction);
        _ui.OpenUi(terminal, ResearchKnowledgeSubmitUI.Key, user);
        _ui.SetUiState(terminal, ResearchKnowledgeSubmitUI.Key, state);
    }

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
            var alreadyKnown = !witness && known.Contains(factId);
            var disabled = locked || alreadyKnown || witnessCap || playerClaim;
            var reason = locked
                ? Loc.GetString("cmu-research-documents-locked")
                : alreadyKnown
                    ? Loc.GetString("cmu-research-documents-already-known")
                    : witnessCap
                        ? Loc.GetString("cmu-research-documents-witness-cap")
                        : playerClaim
                            ? Loc.GetString("cmu-research-documents-player-claimed")
                            : null;
            var cash = !witness && fact.SourceFaction == faction ? fact.Cash : 0;
            var reward = disabled
                ? reason ?? string.Empty
                : witness
                    ? FormatReward(0, fact.SampleData, 0)
                    : FormatReward(fact.Credits, fact.SampleData, cash);
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

    private void OnSubmitMessage(Entity<ResearchDataTerminalComponent> ent, ref ResearchKnowledgeSubmitMessage args)
    {
        var result = SubmitKnowledge(args.Actor, ent.Owner, args.FactIds);
        ShowChemistryResult(args.Actor, ent.Owner, result);
        _ui.CloseUi(ent.Owner, ResearchKnowledgeSubmitUI.Key, args.Actor);
    }

    private void ShowChemistryResult(EntityUid user, EntityUid terminal, ResearchSubmissionResult result)
    {
        if (result.Accepted == 0)
        {
            PopupToUser(
                user,
                result.SkippedReason ?? Loc.GetString("cmu-research-documents-no-source"),
                terminal);
            return;
        }
        PopupToUser(
            user,
            Loc.GetString(
                "cmu-research-documents-submit-result",
                ("accepted", result.Accepted),
                ("skipped", result.Skipped),
                ("credits", result.Credits),
                ("data", result.SampleData),
                ("cash", result.Cash)),
            terminal);
    }

    /// <summary>
    /// Validates and applies a chemistry-terminal submission. Facts may come from personal knowledge or a held
    /// document; possessing the document is enough, so the player does not have to memorize it first.
    /// </summary>
    public ResearchSubmissionResult SubmitKnowledge(
        EntityUid user,
        EntityUid terminal,
        IReadOnlyCollection<string> factIds)
    {
        if (!TryComp<ResearchDataTerminalComponent>(terminal, out var terminalComp))
            return new ResearchSubmissionResult(0, factIds.Count, 0, 0, 0, 0);
        if (factIds.Count > 128)
            return new ResearchSubmissionResult(0, factIds.Count, 0, 0, 0, 0);
        var faction = terminalComp.Faction;
        if (_research.IsResearchBlocked(faction))
            return new ResearchSubmissionResult(
                0,
                factIds.Count,
                0,
                0,
                0,
                0,
                Loc.GetString("cmu-research-documents-locked"));
        var available = GetAvailableFacts(user);
        var research = _research.GetResearch(faction);
        var accepted = 0;
        var skipped = 0;
        var credits = 0;
        var sampleData = 0;
        var cash = 0;
        string? skippedReason = null;
        foreach (var factId in factIds)
        {
            if (!available.ContainsKey(factId) || !_facts.TryGetValue(factId, out var fact))
            {
                skipped++;
                skippedReason ??= Loc.GetString("cmu-research-documents-invalid");
                continue;
            }
            var witness = IsWitnessFact(fact);
            if (witness && _witnessClaims.GetValueOrDefault(factId) >= fact.MaxSubmissions)
            {
                skipped++;
                skippedReason ??= Loc.GetString("cmu-research-documents-witness-cap");
                continue;
            }
            if (witness && _playerClaims.Contains((factId, user)))
            {
                skipped++;
                skippedReason ??= Loc.GetString("cmu-research-documents-player-claimed");
                continue;
            }
            if (!witness && research.KnownFacts.Contains(factId))
            {
                skipped++;
                skippedReason ??= Loc.GetString("cmu-research-documents-already-known");
                continue;
            }

            // Witness facts stay neutral: up to three different players may submit the same sighting, so
            // they are not added to the faction's known facts and do not block later submissions.
            if (!witness)
                research.KnownFacts.Add(factId);
            var factCash = 0;
            if (witness)
            {
                _witnessClaims[factId] = _witnessClaims.GetValueOrDefault(factId) + 1;
                _playerClaims.Add((factId, user));
            }
            else if (fact.SourceFaction == faction)
            {
                if (fact.Kind == ResearchFactKind.Contract && fact.ReagentId is { } reagentId)
                {
                    // The legacy completion path may have paid this reagent already.
                    if (research.RewardedChemicals.Add(reagentId))
                        factCash = fact.Cash;
                }
                else
                {
                    factCash = fact.Cash;
                }
            }
            if (fact.Credits != 0 || fact.SampleData != 0)
            {
                _research.UpdateClearance(
                    _research.GetCredits(faction) + fact.Credits,
                    -1,
                    faction,
                    _research.GetSampleData(faction) + fact.SampleData);
            }
            if (factCash > 0)
                _research.PayFactionCash(faction, factCash, null);
            credits += fact.Credits;
            sampleData += fact.SampleData;
            cash += factCash;
            accepted++;
        }
        if (accepted > 0 || skipped > 0)
        {
            _adminLog.Add(
                LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(user)} submitted research to {ToPrettyString(terminal)} for {faction}: " +
                $"{accepted} accepted, {skipped} skipped, {credits} credits, {sampleData} sample data, ${cash}");
        }
        return new ResearchSubmissionResult(accepted, skipped, credits, sampleData, cash, 0, skippedReason);
    }
}
