using System;
using Content.Shared.CMU14.Chemistry.Research;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.Chemistry.Research;

/// <summary>
/// Read-only list of the player's memorized research facts, opened from the player's own context menu.
/// </summary>
public sealed class ResearchKnowledgeViewBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ResearchKnowledgeSubmitBuiState? _state;
    private ResearchKnowledgeSubmitWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ResearchKnowledgeSubmitWindow>();
        _window.Submit.Visible = false;
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
        _window.Summary.Text = string.Empty;
        _window.Choices.RemoveAllChildren();
        if (state.Choices.Count == 0)
        {
            _window.Choices.AddChild(new Label
            {
                Text = Loc.GetString("cmu-research-documents-review-empty"),
            });
            return;
        }

        foreach (var choice in state.Choices)
        {
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
                Text = string.IsNullOrEmpty(choice.Reward)
                    ? Loc.GetString(
                        "cmu-research-documents-choice-meta-compact",
                        ("category", choice.Category),
                        ("source", choice.Source))
                    : Loc.GetString(
                        "cmu-research-documents-choice-meta",
                        ("category", choice.Category),
                        ("source", choice.Source),
                        ("reward", choice.Reward)),
                ClipText = true,
                StyleClasses = { "LabelSubText" },
            });
        }
    }
}
