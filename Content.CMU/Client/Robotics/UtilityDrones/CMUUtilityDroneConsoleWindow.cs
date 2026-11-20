using System;
using System.Linq;
using System.Numerics;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.Robotics.UtilityDrones;

public sealed class CMUUtilityDroneConsoleWindow : DefaultWindow
{
    public event Action<CMUUtilityConsoleCommandMessage>? Command;

    private readonly TabContainer _tabs = new();
    private readonly TextEdit _scriptEdit = new();
    private readonly BoxContainer _markerList = new();
    private readonly Label _status = new();
    private readonly RichTextLabel _validation = new();
    private readonly Label _lineHint = new();

    private List<CMUUtilityMarkerInfo> _markers = new();
    private bool _dirty;
    private bool _updating;
    private int _lastValidationLine = -1;
    private string _lastValidationError = string.Empty;

    public CMUUtilityDroneConsoleWindow()
    {
        Title = Loc.GetString("cmu-utility-drone-console-title");
        MinSize = new Vector2(760, 700);
        SetSize = new Vector2(920, 860);
        _markerList.Orientation = BoxContainer.LayoutOrientation.Vertical;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };
        _tabs.VerticalExpand = true;
        root.AddChild(_tabs);

        _tabs.AddChild(BuildScriptTab());
        _tabs.AddChild(BuildMarkersTab());
        _tabs.SetTabTitle(0, Loc.GetString("cmu-utility-drone-tab-script"));
        _tabs.SetTabTitle(1, Loc.GetString("cmu-utility-drone-tab-markers"));

        _status.Text = string.Empty;
        _validation.SetMessage(new FormattedMessage());
        _validation.Visible = false;
        _validation.HorizontalExpand = true;
        root.AddChild(_status);
        root.AddChild(_validation);

        Contents.AddChild(root);

        _scriptEdit.OnTextChanged += _ =>
        {
            if (!_updating)
                _dirty = true;
        };
    }

    private Control BuildScriptTab()
    {
        var tab = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };

        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-script-heading") });
        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        var insert = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-insert-command"),
            HorizontalExpand = true,
        };
        insert.OnPressed += _ => OpenCommandWindow(insertMode: true);
        buttons.AddChild(insert);
        var edit = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-edit-line"),
            HorizontalExpand = true,
        };
        edit.OnPressed += _ => OpenCommandWindow(insertMode: false);
        buttons.AddChild(edit);
        tab.AddChild(buttons);

        _scriptEdit.VerticalExpand = true;
        _scriptEdit.MinHeight = 430;
        tab.AddChild(_scriptEdit);

        _lineHint.Text = Loc.GetString("cmu-utility-drone-script-hint");
        tab.AddChild(_lineHint);
        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-script-help") });
        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-script-info-help") });
        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-script-item-hint") });
        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-script-action-help") });
        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-script-event-hint") });

        var commandButtons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        var save = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-save"),
            HorizontalExpand = true,
        };
        save.OnPressed += _ => Send(CMUUtilityConsoleCommand.SaveProgram);
        commandButtons.AddChild(save);
        var print = new Button
        {
            Text = Loc.GetString("cmu-utility-drone-print-board"),
            HorizontalExpand = true,
        };
        print.OnPressed += _ => Send(CMUUtilityConsoleCommand.PrintBoard);
        commandButtons.AddChild(print);
        tab.AddChild(commandButtons);

        return tab;
    }

    private Control BuildMarkersTab()
    {
        var tab = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };

        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-markers-heading") });
        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-marker-hint") });
        tab.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-marker-script-hint") });

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HScrollEnabled = false,
            MinHeight = 360,
        };
        _markerList.HorizontalExpand = true;
        scroll.AddChild(_markerList);
        tab.AddChild(scroll);
        return tab;
    }

    public void UpdateState(CMUUtilityDroneConsoleState state)
    {
        _updating = true;
        try
        {
            _markers = state.Markers;
            _status.Text = state.Status;

            var validationChanged = !string.Equals(
                state.ValidationError,
                _lastValidationError,
                StringComparison.Ordinal) || state.ValidationLine != _lastValidationLine;
            _lastValidationError = state.ValidationError;
            _lastValidationLine = state.ValidationLine;

            var validation = new FormattedMessage();
            if (!string.IsNullOrEmpty(state.ValidationError))
            {
                validation.PushColor(Color.FromHex("#ff6b6b"));
                if (state.ValidationLine > 0)
                {
                    validation.AddText(Loc.GetString(
                        "cmu-utility-drone-error-line",
                        ("line", state.ValidationLine)));
                    validation.AddText(" ");
                }

                validation.AddText(state.ValidationError);
                validation.Pop();
                _validation.Visible = true;
            }
            else
            {
                _validation.Visible = false;
            }

            _validation.SetMessage(validation);

            var scriptMatches = string.Equals(ScriptText, state.Script, StringComparison.Ordinal);
            if (!_dirty && !scriptMatches)
                SetScriptText(state.Script);

            if (string.Equals(ScriptText, state.Script, StringComparison.Ordinal))
                _dirty = false;

            if (validationChanged && state.ValidationLine > 0)
                HighlightErrorLine(state.ValidationLine);

            RebuildMarkers();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Selects the offending source line in the editor so the error is visible
    /// at a glance instead of only in the footer label.
    /// </summary>
    private void HighlightErrorLine(int line)
    {
        if (line <= 0)
            return;

        var text = ScriptText;
        var lineIndex = line - 1;
        var start = 0;
        for (var index = 0; index < lineIndex; index++)
        {
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                start = text.Length;
                break;
            }

            start = newline + 1;
        }

        start = Math.Clamp(start, 0, text.Length);
        var end = text.IndexOf('\n', start);
        if (end < 0)
            end = text.Length;
        else if (end > start && text[end - 1] == '\r')
            end--;

        _scriptEdit.CursorPosition = new TextEdit.CursorPos(end, TextEdit.LineBreakBias.Top);
        _scriptEdit.SelectionStart = new TextEdit.CursorPos(start, TextEdit.LineBreakBias.Top);
        _scriptEdit.GrabKeyboardFocus();
    }

    private void OpenCommandWindow(bool insertMode)
    {
        var window = new CMUUtilityDroneCommandWindow();
        string? replaceLine = null;
        var lineIndex = -1;
        if (!insertMode)
        {
            var lines = SplitLines();
            lineIndex = CurrentLineIndex(lines);
            replaceLine = lineIndex >= 0 && lineIndex < lines.Count ? lines[lineIndex] : string.Empty;
            window.SetCommandText(replaceLine);
        }

        window.CommandReady += text =>
        {
            if (insertMode)
                InsertAtCursor(text);
            else
                ReplaceCurrentLine(lineIndex, text);
        };
        window.OpenCentered();
    }

    private void InsertAtCursor(string text)
    {
        var current = ScriptText;
        var cursor = Math.Clamp(_scriptEdit.CursorPosition.Index, 0, current.Length);
        var prefix = cursor > 0 && current[cursor - 1] != '\n' ? "\n" : string.Empty;
        var suffix = cursor < current.Length && current[cursor] != '\n' ? "\n" : string.Empty;
        SetScriptText(current.Insert(cursor, prefix + text + suffix));
        _dirty = true;
    }

    private void ReplaceCurrentLine(int lineIndex, string text)
    {
        var lines = SplitLines();
        if (lineIndex < 0 || lineIndex >= lines.Count)
            return;

        lines[lineIndex] = text;
        SetScriptText(string.Join('\n', lines));
        _dirty = true;
    }

    private List<string> SplitLines()
    {
        return ScriptText.Replace("\r\n", "\n").Split('\n').ToList();
    }

    private int CurrentLineIndex(List<string> lines)
    {
        var text = ScriptText;
        var cursor = Math.Clamp(_scriptEdit.CursorPosition.Index, 0, text.Length);
        var line = 0;
        for (var i = 0; i < cursor; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return Math.Clamp(line, 0, Math.Max(0, lines.Count - 1));
    }

    private void RebuildMarkers()
    {
        _markerList.RemoveAllChildren();
        for (var i = 0; i < _markers.Count; i++)
        {
            var marker = _markers[i];
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 3,
            };
            row.AddChild(new Label
            {
                Text = Loc.GetString(
                    "cmu-utility-drone-marker-entry",
                    ("number", i + 1),
                    ("state", marker.Anchored
                        ? Loc.GetString("cmu-utility-drone-marker-anchored")
                        : Loc.GetString("cmu-utility-drone-marker-unanchored"))),
                HorizontalExpand = true,
            });
            var unlink = new Button { Text = Loc.GetString("cmu-utility-drone-marker-unlink") };
            unlink.OnPressed += _ => Send(CMUUtilityConsoleCommand.UnlinkMarker, marker.Marker);
            row.AddChild(unlink);
            _markerList.AddChild(row);
        }
    }

    private void Send(CMUUtilityConsoleCommand command, NetEntity marker = default)
    {
        Command?.Invoke(new CMUUtilityConsoleCommandMessage(
            command,
            BuildProgramInfo(),
            marker));
    }

    private CMUUtilityProgramInfo BuildProgramInfo()
    {
        return new CMUUtilityProgramInfo(
            new List<CMUUtilityStepInfo>(),
            new List<CMUUtilityRuleInfo>(),
            false,
            30f)
        {
            Script = ScriptText,
        };
    }

    private string ScriptText => Rope.Collapse(_scriptEdit.TextRope);

    private void SetScriptText(string text)
    {
        _scriptEdit.TextRope = new Rope.Leaf(text);
    }
}
