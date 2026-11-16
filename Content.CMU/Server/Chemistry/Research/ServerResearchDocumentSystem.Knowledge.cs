using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Shared.Player;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private void InitializeKnowledge()
    {
        SubscribeLocalEvent<ResearchDocumentComponent, GetVerbsEvent<AlternativeVerb>>(OnDocumentVerbs);
        SubscribeLocalEvent<ResearchDocumentComponent, UseInHandEvent>(OnDocumentUseInHand);
        SubscribeLocalEvent<ResearchDocumentComponent, ResearchDocumentReadDoAfterEvent>(OnReadComplete);
        SubscribeLocalEvent<ActorComponent, GetVerbsEvent<AlternativeVerb>>(OnPlayerKnowledgeVerbs);
    }

    private void OnPlayerKnowledgeVerbs(Entity<ActorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (args.Target != args.User || !args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-research-documents-review-verb"),
            Act = () => OpenKnowledgeView(user, ent.Owner),
        });
    }

    private void OpenKnowledgeView(EntityUid user, EntityUid player)
    {
        var choices = new List<ResearchKnowledgeChoice>();
        if (TryComp<ResearchKnowledgeComponent>(user, out var knowledge))
        {
            foreach (var factId in knowledge.Facts)
            {
                if (!_facts.TryGetValue(factId, out var fact))
                    continue;
                choices.Add(new ResearchKnowledgeChoice(
                    fact.Id,
                    fact.Title,
                    CategoryName(fact),
                    Loc.GetString("cmu-research-documents-source-personal"),
                    string.Empty,
                    false,
                    null));
            }
        }
        _ui.OpenUi(player, ResearchKnowledgeViewUI.Key, user);
        _ui.SetUiState(
            player,
            ResearchKnowledgeViewUI.Key,
            new ResearchKnowledgeSubmitBuiState(
                false,
                Loc.GetString("cmu-research-documents-review-title"),
                choices));
    }

    private void OnDocumentVerbs(Entity<ResearchDocumentComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!ent.Comp.ScannerReport || !args.CanAccess || !args.CanInteract || !IsValidWitness(args.User))
            return;
        EnsureScannerReportFacts(ent.Owner, ent.Comp);
        var user = args.User;
        var newFacts = CountNewFacts(user, ent.Comp.FactIds);
        var disabled = newFacts == 0;
        string? message = null;
        if (disabled)
        {
            message = ent.Comp.FactIds.Count == 0
                ? Loc.GetString("cmu-research-documents-no-facts")
                : Loc.GetString("cmu-research-documents-remember-none");
        }
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-research-documents-remember-verb"),
            Disabled = disabled,
            Message = message,
            Act = () => RememberDocument(user, ent.Owner),
        });
    }

    /// <summary>
    /// Scanner reports are not knowledge until the player explicitly memorizes them. The report stays in place.
    /// </summary>
    public bool RememberDocument(EntityUid user, EntityUid paper)
    {
        if (!IsValidWitness(user)
            || !TryComp<ResearchDocumentComponent>(paper, out var document)
            || !document.ScannerReport)
        {
            return false;
        }
        EnsureScannerReportFacts(paper, document);
        if (document.FactIds.Count == 0)
        {
            PopupToUser(user, Loc.GetString("cmu-research-documents-no-facts"), paper);
            return false;
        }
        var added = AddKnowledge(user, document.FactIds);
        if (added == 0)
        {
            PopupToUser(user, Loc.GetString("cmu-research-documents-remember-none"), paper);
            return false;
        }
        PopupToUser(
            user,
            Loc.GetString("cmu-research-documents-remember-result", ("count", added)),
            paper);
        _adminLog.Add(
            LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(user)} memorized {added} research facts from {ToPrettyString(paper)}");
        return true;
    }

    private void OnDocumentUseInHand(Entity<ResearchDocumentComponent> ent, ref UseInHandEvent args)
    {
        // Scanner reports deliberately use the explicit Remember verb instead of automatic reading.
        if (ent.Comp.ScannerReport || !IsValidWitness(args.User))
            return;
        var newFacts = CountNewFacts(args.User, ent.Comp.FactIds);
        if (newFacts == 0)
        {
            PopupToUser(args.User, Loc.GetString("cmu-research-documents-remember-none"), ent);
            return;
        }
        var doAfter = new DoAfterArgs(
            EntityManager,
            args.User,
            TimeSpan.FromSeconds(2),
            new ResearchDocumentReadDoAfterEvent(),
            ent.Owner,
            target: ent.Owner)
        {
            NeedHand = true,
            BreakOnDropItem = true,
        };
        if (_doAfter.TryStartDoAfter(doAfter))
            PopupToUser(args.User, Loc.GetString("cmu-research-documents-read-start"), ent);
        // Do not mark the interaction handled: the normal paper window may open while the read runs.
    }

    private void OnReadComplete(Entity<ResearchDocumentComponent> ent, ref ResearchDocumentReadDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !IsValidWitness(args.User))
            return;
        args.Handled = true;
        var added = AddKnowledge(args.User, ent.Comp.FactIds);
        if (added == 0)
        {
            PopupToUser(args.User, Loc.GetString("cmu-research-documents-remember-none"), ent);
            return;
        }
        PopupToUser(
            args.User,
            Loc.GetString("cmu-research-documents-read-done", ("count", added)),
            ent);
        _adminLog.Add(
            LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.User)} read {ToPrettyString(ent.Owner)} and learned {added} research facts");
    }
}
