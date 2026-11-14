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
    private Vector2 _mousePosition;
    private bool _hasMouse;

    /// <summary>Localized message shown before the first projection.</summary>
    public string EmptyText = string.Empty;

    public CMUQueueHistoryControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = cache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 10);
        MinHeight = 170;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mousePosition = args.RelativePixelPosition;
        _hasMouse = true;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hasMouse = false;
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

        if (_hasMouse && _history.Count > 0)
        {
            var mouse = new Vector2(box.Left + _mousePosition.X, box.Top + _mousePosition.Y);
            if (mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom)
            {
                var bestDistance = float.MaxValue;
                CMUQueueHistoryInfo? bestPoint = null;
                var bestNode = 0;
                var bestX = 0f;
                var bestY = 0f;

                foreach (var point in _history)
                {
                    var x = left + (right - left) * (point.Minute / maxTime);
                    for (var nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
                    {
                        var queue = nodeIndex < point.Queues.Count ? point.Queues[nodeIndex] : 0;
                        var y = bottom - (bottom - top) * (float)(queue / maxQueue);
                        var distance = Vector2.DistanceSquared(mouse, new Vector2(x, y));
                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        bestPoint = point;
                        bestNode = nodeIndex;
                        bestX = x;
                        bestY = y;
                    }
                }

                if (bestPoint != null)
                {
                    handle.DrawLine(
                        new Vector2(bestX, top),
                        new Vector2(bestX, bottom),
                        CrtTerminalPalette.TextDim);
                    handle.DrawRect(
                        new UIBox2(bestX - 3, bestY - 3, bestX + 3, bestY + 3),
                        ColorFor(bestNode));

                    var queue = bestNode < bestPoint.Queues.Count ? bestPoint.Queues[bestNode] : 0;
                    var label = Loc.GetString(
                        "cmu-queue-console-hover-point",
                        ("name", NodeName(_nodes[bestNode])),
                        ("minute", bestPoint.Minute.ToString("0.00")),
                        ("queue", queue.ToString("0.00")));
                    var labelWidth = label.Length * 6.1f;
                    var minLabelX = left + labelWidth / 2 + 4;
                    var maxLabelX = right - labelWidth / 2 - 4;
                    var labelX = maxLabelX >= minLabelX
                        ? Math.Clamp((left + right) / 2, minLabelX, maxLabelX)
                        : (left + right) / 2;
                    DrawHoverLabel(
                        handle,
                        new Vector2(labelX, top + 8),
                        label,
                        labelWidth,
                        CrtTerminalPalette.TextBright);
                }
            }
        }
    }

    private void DrawHoverLabel(
        DrawingHandleScreen handle,
        Vector2 position,
        string text,
        float width,
        Color color)
    {
        handle.DrawRect(
            new UIBox2(position.X - width / 2 - 3, position.Y - 8, position.X + width / 2 + 3, position.Y + 5),
            CrtTerminalPalette.Surface0);
        handle.DrawString(
            _font,
            new Vector2(position.X - width / 2, position.Y - 7),
            text,
            color);
    }

    private static string NodeName(CMUQueueNodeInfo node)
    {
        var name = Loc.TryGetString(node.Name, out var fullName) ? fullName : node.Id;
        return string.IsNullOrEmpty(node.Suffix) ? name : $"{name} {node.Suffix}";
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
