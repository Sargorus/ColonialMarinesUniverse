using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Fax.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Paper;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private void InitializeWriting()
    {
        SubscribeLocalEvent<PaperComponent, GetVerbsEvent<AlternativeVerb>>(OnPaperVerbs);
        SubscribeLocalEvent<PaperComponent, ResearchDocumentWriteDoAfterEvent>(OnWriteComplete);
        Subs.BuiEvents<PaperComponent>(ResearchDocumentWriteUI.Key,
            subs =>
            {
                subs.Event<ResearchDocumentWriteMessage>(OnWriteMessage);
            });
    }

    private void OnPaperVerbs(Entity<PaperComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        if (HasComp<ResearchDocumentComponent>(ent) || !IsBlank(ent.Comp))
            return;
        if (!HasPen(args.User, args.Using))
            return;
        var user = args.User;
        if (!TryComp<ResearchKnowledgeComponent>(user, out var knowledge) || knowledge.Facts.Count == 0)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cmu-research-documents-write-verb"),
                Disabled = true,
                Message = Loc.GetString("cmu-research-documents-no-source"),
            });
            return;
        }
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-research-documents-write-verb"),
            Act = () => OpenWriteUi(user, ent.Owner),
        });
    }

    private static bool IsBlank(PaperComponent paper)
    {
        return string.IsNullOrWhiteSpace(paper.Content)
               && !paper.EditingDisabled
               && paper.StampedBy.Count == 0;
    }

    private bool HasPen(EntityUid user, EntityUid? usingItem)
    {
        if (usingItem is { } used && _tags.HasTag(used, WriteTag))
            return true;
        return TryGetPen(user, out _);
    }

    private bool TryGetPen(EntityUid user, out EntityUid pen)
    {
        if (TryComp<HandsComponent>(user, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((user, hands)))
            {
                if (!_tags.HasTag(held, WriteTag))
                    continue;
                pen = held;
                return true;
            }
        }
        pen = EntityUid.Invalid;
        return false;
    }

    private void OpenWriteUi(EntityUid user, EntityUid paper)
    {
        if (!TryComp<ResearchKnowledgeComponent>(user, out var knowledge))
            return;
        var choices = new List<ResearchKnowledgeChoice>();
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
        if (choices.Count == 0)
        {
            PopupToUser(user, Loc.GetString("cmu-research-documents-no-source"), paper);
            return;
        }
        _ui.OpenUi(paper, ResearchDocumentWriteUI.Key, user);
        _ui.SetUiState(paper, ResearchDocumentWriteUI.Key, new ResearchDocumentWriteBuiState(choices));
    }

    private void OnWriteMessage(Entity<PaperComponent> ent, ref ResearchDocumentWriteMessage args)
    {
        var user = args.Actor;
        if (!IsBlank(ent.Comp) || HasComp<ResearchDocumentComponent>(ent))
            return;
        if (!Enum.IsDefined(args.Type) || args.FactIds.Count > 64)
            return;
        if (!TryGetPen(user, out var pen))
        {
            PopupToUser(user, Loc.GetString("cmu-research-documents-pen-required"), ent);
            return;
        }
        var selected = SelectKnownFacts(user, args.FactIds);
        if (selected.Count == 0)
        {
            PopupToUser(user, Loc.GetString("cmu-research-documents-no-source"), ent);
            return;
        }
        var title = args.Title.Trim();
        if (title.Length > 80)
            title = title[..80];
        if (title.Length == 0)
            title = Loc.GetString("cmu-research-documents-default-title", ("type", DocumentTypeName(args.Type)));
        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            TimeSpan.FromSeconds(3),
            new ResearchDocumentWriteDoAfterEvent
            {
                Title = title,
                Type = args.Type,
                FactIds = selected,
            },
            ent.Owner,
            target: ent.Owner,
            used: pen)
        {
            NeedHand = true,
            BreakOnDropItem = true,
        };
        if (!_doAfter.TryStartDoAfter(doAfter))
            return;
        _ui.CloseUi(ent.Owner, ResearchDocumentWriteUI.Key, user);
        PopupToUser(user, Loc.GetString("cmu-research-documents-write-start"), ent);
    }

    private List<string> SelectKnownFacts(EntityUid user, IEnumerable<string> factIds)
    {
        var selected = new List<string>();
        if (!TryComp<ResearchKnowledgeComponent>(user, out var knowledge))
            return selected;
        var seen = new HashSet<string>();
        foreach (var factId in factIds)
        {
            if (!knowledge.Facts.Contains(factId) || !_facts.ContainsKey(factId) || !seen.Add(factId))
                continue;
            selected.Add(factId);
        }
        return selected;
    }

    private void OnWriteComplete(Entity<PaperComponent> ent, ref ResearchDocumentWriteDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;
        args.Handled = true;
        if (!IsBlank(ent.Comp) || HasComp<ResearchDocumentComponent>(ent))
            return;
        if (!TryGetPen(args.User, out _))
        {
            PopupToUser(args.User, Loc.GetString("cmu-research-documents-pen-required"), ent);
            return;
        }
        var selected = SelectKnownFacts(args.User, args.FactIds);
        if (selected.Count == 0)
            return;
        var title = args.Title.Trim();
        if (title.Length > 80)
            title = title[..80];
        if (title.Length == 0)
            title = Loc.GetString("cmu-research-documents-default-title", ("type", DocumentTypeName(args.Type)));
        var document = EnsureComp<ResearchDocumentComponent>(ent.Owner);
        document.FactIds = selected;
        document.ScannerReport = false;
        document.Author = Name(args.User);
        document.Title = title;
        document.Type = args.Type;
        document.CreatedAt = _timing.CurTime;
        var text = BuildDocumentText(title, document.Author, document.Type, selected);
        _paper.SetContent(ent.Owner, text);
        ent.Comp.EditingDisabled = true;
        _meta.SetEntityName(ent.Owner, title);
        _meta.SetEntityDescription(ent.Owner, Loc.GetString("cmu-research-documents-description"));
        RemComp<FaxableObjectComponent>(ent.Owner);
        Dirty(ent.Owner, ent.Comp);
        PopupToUser(
            args.User,
            Loc.GetString("cmu-research-documents-write-done", ("title", title)),
            ent);
        _adminLog.Add(
            LogType.Action,
            LogImpact.Medium,
            $"{ToPrettyString(args.User)} wrote research document {ToPrettyString(ent.Owner)} " +
            $"({document.Type}) with {selected.Count} facts: {string.Join(", ", selected)}");
    }
}
