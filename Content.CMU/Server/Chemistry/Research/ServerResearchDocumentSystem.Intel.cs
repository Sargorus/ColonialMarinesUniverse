using System;
using System.Collections.Generic;
using Content.Shared._RMC14.Intel;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Verbs;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private void InitializeIntel()
    {
        SubscribeLocalEvent<IntelConsoleComponent, GetVerbsEvent<AlternativeVerb>>(OnIntelVerbs);
        Subs.BuiEvents<IntelConsoleComponent>(ResearchKnowledgeSubmitUI.Key,
            subs =>
            {
                subs.Event<ResearchKnowledgeSubmitMessage>(OnIntelSubmitMessage);
            });
    }

    private void OnIntelVerbs(Entity<IntelConsoleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || HasComp<XenoComponent>(args.User))
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-research-documents-submit-intel-verb"),
            Act = () => OpenIntelUi(user, ent.Owner),
        });
    }

    private void OpenIntelUi(EntityUid user, EntityUid console)
    {
        if (!TryComp<IntelConsoleComponent>(console, out var consoleComp))
            return;
        var state = BuildIntelState(user, consoleComp.Team);
        _ui.OpenUi(console, ResearchKnowledgeSubmitUI.Key, user);
        _ui.SetUiState(console, ResearchKnowledgeSubmitUI.Key, state);
    }

    private ResearchKnowledgeSubmitBuiState BuildIntelState(EntityUid user, string team)
    {
        var available = GetAvailableFacts(user);
        _intelClaims.TryGetValue(team, out var claims);
        var choices = new List<ResearchKnowledgeChoice>();
        foreach (var (factId, source) in available)
        {
            if (!_facts.TryGetValue(factId, out var fact))
                continue;
            var witness = IsWitnessFact(fact);
            var teamClaim = claims?.Contains(factId) == true;
            var witnessCap = witness && _witnessClaims.GetValueOrDefault(factId) >= fact.MaxSubmissions;
            var playerClaim = witness && _playerClaims.Contains((factId, user));
            var disabled = teamClaim || witnessCap || playerClaim;
            var reason = teamClaim
                ? Loc.GetString("cmu-research-documents-already-known")
                : witnessCap
                    ? Loc.GetString("cmu-research-documents-witness-cap")
                    : playerClaim
                        ? Loc.GetString("cmu-research-documents-player-claimed")
                        : null;
            var ownGovforResearch = IsChemistryFact(fact)
                                    && string.Equals(fact.SourceFaction, "govfor", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(team, "govfor", StringComparison.OrdinalIgnoreCase);
            var reward = disabled
                ? reason ?? string.Empty
                : ownGovforResearch
                    ? Loc.GetString("cmu-research-documents-no-intel-own")
                    : Loc.GetString(
                        "cmu-research-documents-intel-reward",
                        ("points", fact.IntelPoints.ToString("0.##")));
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
            false,
            Loc.GetString("cmu-research-documents-submit-intel-title", ("team", team)),
            choices);
    }

    private void OnIntelSubmitMessage(Entity<IntelConsoleComponent> ent, ref ResearchKnowledgeSubmitMessage args)
    {
        var result = SubmitIntelKnowledge(args.Actor, ent.Owner, args.FactIds);
        ShowIntelResult(args.Actor, ent.Owner, result);
        _ui.CloseUi(ent.Owner, ResearchKnowledgeSubmitUI.Key, args.Actor);
    }

    private void ShowIntelResult(EntityUid user, EntityUid console, ResearchSubmissionResult result)
    {
        if (result.Accepted == 0)
        {
            PopupToUser(
                user,
                result.SkippedReason ?? Loc.GetString("cmu-research-documents-no-source"),
                console);
            return;
        }
        PopupToUser(
            user,
            Loc.GetString(
                "cmu-research-documents-intel-submit-result",
                ("accepted", result.Accepted),
                ("skipped", result.Skipped),
                ("points", result.IntelPoints.ToString("0.##"))),
            console);
    }

    /// <summary>
    /// Credits research facts to the intel team of the console. GOVFOR cannot convert its own chemistry facts
    /// into intel points; witness facts are neutral and remain valid.
    /// </summary>
    public ResearchSubmissionResult SubmitIntelKnowledge(
        EntityUid user,
        EntityUid console,
        IReadOnlyCollection<string> factIds)
    {
        if (!TryComp<IntelConsoleComponent>(console, out var consoleComp))
            return new ResearchSubmissionResult(0, factIds.Count, 0, 0, 0, 0);
        if (factIds.Count > 128)
            return new ResearchSubmissionResult(0, factIds.Count, 0, 0, 0, 0);
        var team = consoleComp.Team;
        var available = GetAvailableFacts(user);
        if (!_intelClaims.TryGetValue(team, out var claims))
        {
            claims = [];
            _intelClaims[team] = claims;
        }
        var accepted = 0;
        var skipped = 0;
        var pointsTotal = 0f;
        string? skippedReason = null;
        foreach (var factId in factIds)
        {
            if (!available.ContainsKey(factId) || !_facts.TryGetValue(factId, out var fact))
            {
                skipped++;
                skippedReason ??= Loc.GetString("cmu-research-documents-invalid");
                continue;
            }
            if (claims.Contains(factId))
            {
                skipped++;
                skippedReason ??= Loc.GetString("cmu-research-documents-already-known");
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

            claims.Add(factId);
            if (witness)
            {
                _witnessClaims[factId] = _witnessClaims.GetValueOrDefault(factId) + 1;
                _playerClaims.Add((factId, user));
            }
            var ownGovforResearch = IsChemistryFact(fact)
                                    && string.Equals(fact.SourceFaction, "govfor", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(team, "govfor", StringComparison.OrdinalIgnoreCase);
            if (!ownGovforResearch && fact.IntelPoints > 0)
            {
                var tree = _intel.EnsureTechTree(team);
                _intel.AddPoints(tree, FixedPoint2.New(fact.IntelPoints), team);
                pointsTotal += fact.IntelPoints;
            }
            accepted++;
        }
        if (accepted > 0 || skipped > 0)
        {
            _adminLog.Add(
                LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(user)} submitted research to intel console {ToPrettyString(console)} for {team}: " +
                $"{accepted} accepted, {skipped} skipped, {pointsTotal:0.##} intel points");
        }
        return new ResearchSubmissionResult(accepted, skipped, 0, 0, 0, pointsTotal, skippedReason);
    }
}
