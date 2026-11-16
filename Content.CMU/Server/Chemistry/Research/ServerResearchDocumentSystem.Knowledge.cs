using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private void InitializeKnowledge()
    {
        SubscribeLocalEvent<ResearchDocumentComponent, GetVerbsEvent<AlternativeVerb>>(OnDocumentVerbs);
        SubscribeLocalEvent<ResearchDocumentComponent, UseInHandEvent>(OnDocumentUseInHand);
        SubscribeLocalEvent<ResearchDocumentComponent, ResearchDocumentReadDoAfterEvent>(OnReadComplete);
    }

    private void OnDocumentVerbs(Entity<ResearchDocumentComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!ent.Comp.ScannerReport || !args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        var newFacts = CountNewFacts(user, ent.Comp.FactIds);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-research-documents-remember-verb"),
            Disabled = newFacts == 0,
            Message = newFacts == 0 ? Loc.GetString("cmu-research-documents-remember-none") : null,
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
        if (args.Handled || args.Cancelled)
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
