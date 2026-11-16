using Content.Shared.CMU14.Chemistry.Research;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.Chemistry.Research;

public sealed partial class ResearchDataTerminalBui
{
    private void RefreshKnowledge(ResearchDataTerminalBuiState state, StyleBoxFlat panel)
    {
        if (_window is null)
            return;

        _window.KnowledgeList.RemoveAllChildren();
        if (state.Knowledge.Count == 0)
        {
            _window.KnowledgeList.AddChild(new RichTextLabel
            {
                Text = Loc.GetString("research-data-ui-knowledge-empty"),
                Margin = new Thickness(5f),
            });
            return;
        }

        foreach (var entry in state.Knowledge)
        {
            var body = Loc.GetString(
                "research-data-ui-knowledge-entry",
                ("category", entry.Category),
                ("reward", entry.Reward),
                ("detail", entry.Detail));
            _window.KnowledgeList.AddChild(MakeEntry(entry.Title, body, panel));
        }
    }
}
