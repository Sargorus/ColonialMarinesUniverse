using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.Chemistry.Research;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.Chemistry.Research;

public sealed class ResearchDocumentWriteBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private readonly HashSet<string> _selected = [];
    private ResearchDocumentWriteWindow? _window;
    private ResearchDocumentWriteBuiState? _state;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ResearchDocumentWriteWindow>();
        foreach (var type in Enum.GetValues<ResearchDocumentType>())
        {
            _window.TypeOption.AddItem(
                Loc.GetString($"cmu-research-documents-type-{type.ToString().ToLowerInvariant()}"),
                (int)type);
        }
        _window.TypeOption.SelectId((int)ResearchDocumentType.Report);
        _window.Write.OnPressed += _ => Write();
        if (_state is not null)
            Refresh(_state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not ResearchDocumentWriteBuiState writeState)
            return;
        _state = writeState;
        Refresh(writeState);
    }

    private void Refresh(ResearchDocumentWriteBuiState state)
    {
        if (_window is null)
            return;
        _selected.Clear();
        _window.Facts.RemoveAllChildren();
        foreach (var choice in state.Facts)
        {
            var check = new CheckBox
            {
                Text = choice.Title,
                Pressed = true,
                ClipText = true,
                ToolTip = choice.Title,
            };
            _selected.Add(choice.FactId);
            check.OnToggled += args =>
            {
                if (args.Pressed)
                    _selected.Add(choice.FactId);
                else
                    _selected.Remove(choice.FactId);
                UpdateWrite();
            };
            _window.Facts.AddChild(check);
        }
        UpdateWrite();
    }

    private void UpdateWrite()
    {
        if (_window is not null)
            _window.Write.Disabled = _selected.Count == 0;
    }

    private void Write()
    {
        if (_window is null || _selected.Count == 0)
            return;
        var type = (ResearchDocumentType)_window.TypeOption.SelectedId;
        SendMessage(new ResearchDocumentWriteMessage(_window.TitleEdit.Text.Trim(), type, _selected.ToList()));
        Close();
    }
}
