using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.Chemistry.Research;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.Chemistry.Research;

public sealed class ResearchKnowledgeSubmitBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private readonly HashSet<string> _selected = [];
    private ResearchKnowledgeSubmitBuiState? _state;
    private ResearchKnowledgeSubmitWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ResearchKnowledgeSubmitWindow>();
        _window.Submit.OnPressed += _ => Submit();
        if (_state is not null)
            Refresh(_state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not ResearchKnowledgeSubmitBuiState submitState)
            return;
        _state = submitState;
        Refresh(submitState);
    }

    private void Refresh(ResearchKnowledgeSubmitBuiState state)
    {
        if (_window is null)
            return;
        _selected.Clear();
        _window.Title = state.Title;
        _window.Summary.Text = state.Locked ? Loc.GetString("cmu-research-documents-locked") : string.Empty;
        _window.Choices.RemoveAllChildren();
        if (state.Choices.Count == 0)
        {
            _window.Choices.AddChild(new Label { Text = Loc.GetString("cmu-research-documents-no-source") });
            _window.Submit.Disabled = true;
            return;
        }

        foreach (var choice in state.Choices)
        {
            var check = new CheckBox
            {
                Text = choice.Title,
                Pressed = !choice.Disabled,
                Disabled = choice.Disabled,
                ClipText = true,
                ToolTip = choice.DisabledReason,
            };
            if (!choice.Disabled)
                _selected.Add(choice.FactId);
            check.OnToggled += args =>
            {
                if (args.Pressed)
                    _selected.Add(choice.FactId);
                else
                    _selected.Remove(choice.FactId);
                UpdateSubmit();
            };
            _window.Choices.AddChild(check);
            _window.Choices.AddChild(new Label
            {
                Text = Loc.GetString(
                    "cmu-research-documents-choice-meta",
                    ("category", choice.Category),
                    ("source", choice.Source),
                    ("reward", choice.Reward)),
                ClipText = true,
                ToolTip = choice.Reward,
                StyleClasses = { "LabelSubText" },
            });
        }
        UpdateSubmit();
    }

    private void UpdateSubmit()
    {
        if (_window is not null)
            _window.Submit.Disabled = _selected.Count == 0;
    }

    private void Submit()
    {
        if (_selected.Count == 0)
            return;
        SendMessage(new ResearchKnowledgeSubmitMessage(_selected.ToList()));
    }
}
