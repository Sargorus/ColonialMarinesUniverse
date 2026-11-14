using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.CMU14.Interface;
using Content.Client.Resources;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Draws the projected queue length of every node over the diagnostic
/// timeline. The chart is deliberately simple: a node whose line climbs is
/// overloaded, a node whose line stays at zero is keeping up.
/// </summary>
public sealed class CMUQueueHistoryControl : Control
{
    private static readonly Color[] Palette =
    {
        new(0.41f, 0.76f, 1.00f),
        new(0.49f, 0.91f, 0.53f),
        new(0.95f, 0.80f, 0.38f),
        new(1.00f, 0.61f, 0.42f),
        new(1.00f, 0.48f, 0.45f),
        new(0.82f, 0.66f, 1.00f),
    };

    private readonly Font _font;
    private IReadOnlyList<CMUQueueNodeInfo> _nodes = Array.Empty<CMUQueueNodeInfo>();
    private IReadOnlyList<CMUQueueHistoryInfo> _history = Array.Empty<CMUQueueHistoryInfo>();
    private int _elapsed;

    /// <summary>Localized message shown before the first projection.</summary>
    public string EmptyText = string.Empty;

    public CMUQueueHistoryControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = cache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 10);
        MinHeight = 170;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    public static Color ColorFor(int index)
    {
        return Palette[index % Palette.Length];
    }

    public void SetData(
        IReadOnlyList<CMUQueueNodeInfo> nodes,
        IReadOnlyList<CMUQueueHistoryInfo> history,
        int elapsed)
    {
        _nodes = nodes;
        _history = history;
        _elapsed = elapsed;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawRect(box, CrtTerminalPalette.Surface0);

        var left = box.Left + 42;
        var right = box.Right - 12;
        var top = box.Top + 12;
        var bottom = box.Bottom - 24;

        if (right <= left || bottom <= top)
            return;

        handle.DrawLine(new Vector2(left, top), new Vector2(left, bottom), CrtTerminalPalette.Line);
        handle.DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), CrtTerminalPalette.Line);

        if (_nodes.Count == 0 || _history.Count < 2)
        {
            handle.DrawString(
                _font,
                new Vector2(left + 8, top + 8),
                EmptyText,
                CrtTerminalPalette.TextDim);
            return;
        }

        var maxTime = MathF.Max(1, _elapsed);
        foreach (var point in _history)
            maxTime = MathF.Max(maxTime, point.Minute);

        var maxQueue = 1.0;
        foreach (var point in _history)
        {
            foreach (var queue in point.Queues)
                maxQueue = Math.Max(maxQueue, queue);
        }

        maxQueue = NiceCeil(maxQueue * 1.1);

        for (var line = 0; line <= 2; line++)
        {
            var fraction = line / 2f;
            var y = bottom - (bottom - top) * fraction;
            handle.DrawLine(new Vector2(left, y), new Vector2(right, y), CrtTerminalPalette.Surface2);

            var label = (maxQueue * fraction).ToString("0.#");
            handle.DrawString(_font, new Vector2(box.Left + 2, y - 6), label, CrtTerminalPalette.TextDim);
        }

        DrawTimeLabel(handle, left, bottom, "0");
        DrawTimeLabel(handle, (left + right) / 2, bottom, $"{maxTime / 2f:0.#}");
        DrawTimeLabel(handle, right, bottom, maxTime.ToString("0.#"));

        for (var nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
        {
            var color = ColorFor(nodeIndex);
            Vector2? previous = null;

            foreach (var point in _history)
            {
                var queue = nodeIndex < point.Queues.Count ? point.Queues[nodeIndex] : 0;
                var x = left + (right - left) * (point.Minute / maxTime);
                var y = bottom - (bottom - top) * (float)(queue / maxQueue);
                var current = new Vector2(x, y);

                if (previous != null)
                    handle.DrawLine(previous.Value, current, color);

                handle.DrawRect(
                    new UIBox2(current.X - 2, current.Y - 2, current.X + 2, current.Y + 2),
                    color);
                previous = current;
            }
        }
    }

    private void DrawTimeLabel(
        DrawingHandleScreen handle,
        float x,
        float bottom,
        string text)
    {
        handle.DrawString(
            _font,
            new Vector2(x - text.Length * 3f, bottom + 5),
            text,
            CrtTerminalPalette.TextDim);
    }

    private static double NiceCeil(double value)
    {
        if (value <= 1)
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var nice = normalized <= 1
            ? 1
            : normalized <= 2
                ? 2
                : normalized <= 5
                    ? 5
                    : 10;

        return nice * magnitude;
    }
}
