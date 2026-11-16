using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.Chemistry.Research;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.Chemistry.Research;

/// <summary>
/// Read-only list of the player's memorized research facts, opened from the player's own context menu.
/// The search and category controls filter client-side; the server already sent the complete list.
/// </summary>
public sealed class ResearchKnowledgeViewBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private readonly List<string> _categories = [];
    private ResearchKnowledgeSubmitBuiState? _state;
    private ResearchKnowledgeViewWindow? _window;
    private string _search = string.Empty;
    private string? _category;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ResearchKnowledgeViewWindow>();
        _search = string.Empty;
        _category = null;
        _window.Search.Text = string.Empty;
        _window.Search.OnTextChanged += args =>
        {
            _search = args.Text;
            RebuildList();
        };
        _window.Category.OnItemSelected += args =>
        {
            _window.Category.SelectId(args.Id);
            _category = args.Id == 0 ? null : _categories[args.Id - 1];
            RebuildList();
        };
        if (_state is not null)
            Refresh(_state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not ResearchKnowledgeSubmitBuiState viewState)
            return;
        _state = viewState;
        Refresh(viewState);
    }

    private void Refresh(ResearchKnowledgeSubmitBuiState state)
    {
        if (_window is null)
            return;
        _window.Title = state.Title;
        RefreshCategories(state);
        RebuildList();
    }

    private void RefreshCategories(ResearchKnowledgeSubmitBuiState state)
    {
        if (_window is null)
            return;
        var previous = _category;
        _categories.Clear();
        _window.Category.Clear();
        _window.Category.AddItem(Loc.GetString("cmu-research-documents-view-all"), 0);
        foreach (var category in state.Choices
                     .Select(choice => choice.Category)
                     .Distinct()
                     .OrderBy(category => category, StringComparer.CurrentCulture))
        {
            _categories.Add(category);
            _window.Category.AddItem(category, _categories.Count);
        }

        var index = previous is null ? 0 : _categories.IndexOf(previous) + 1;
        _window.Category.SelectId(index < 0 ? 0 : index);
        _category = index <= 0 ? null : previous;
    }

    private void RebuildList()
    {
        if (_window is null || _state is null)
            return;

        _window.Choices.RemoveAllChildren();
        var query = _search.Trim();
        var shown = 0;
        foreach (var choice in _state.Choices)
        {
            if (_category is not null
                && !string.Equals(choice.Category, _category, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (query.Length > 0
                && choice.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0
                && choice.Category.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
            {
                continue;
            }

            AddChoice(choice);
            shown++;
        }

        _window.Summary.Text = Loc.GetString(
            "cmu-research-documents-view-count",
            ("shown", shown),
            ("total", _state.Choices.Count));
        if (shown == 0)
        {
            _window.Choices.AddChild(new Label
            {
                Text = Loc.GetString("cmu-research-documents-view-no-results"),
            });
        }
    }

    private void AddChoice(ResearchKnowledgeChoice choice)
    {
        if (_window is null)
            return;
        var check = new CheckBox
        {
            Text = choice.Title,
            Pressed = false,
            Disabled = true,
            ClipText = true,
            HorizontalExpand = true,
            ToolTip = choice.Title,
        };
        // ClipText measures the label at zero width; expand it so the title remains visible.
        check.Label.HorizontalExpand = true;
        _window.Choices.AddChild(check);
        _window.Choices.AddChild(new Label
        {
            Text = Loc.GetString(
                "cmu-research-documents-choice-meta-compact",
                ("category", choice.Category),
                ("source", choice.Source)),
            ClipText = true,
            StyleClasses = { "LabelSubText" },
        });
    }
}
