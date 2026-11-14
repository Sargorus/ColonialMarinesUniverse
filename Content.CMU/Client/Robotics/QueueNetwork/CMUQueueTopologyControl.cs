using System;
using System.Collections.Generic;
using System.Linq;
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
/// Draws the generated queueing network: external supply, nodes, routing
/// arrows, the sink and available intake diversions. The layout is authored
/// by the generator through node column/row positions.
/// </summary>
public sealed class CMUQueueTopologyControl : Control
{
    private readonly Font _font;
    private IReadOnlyList<CMUQueueNodeInfo> _nodes = Array.Empty<CMUQueueNodeInfo>();
    private IReadOnlyList<CMUQueueRouteInfo> _routes = Array.Empty<CMUQueueRouteInfo>();
    private IReadOnlyList<CMUQueueBypassInfo> _bypasses = Array.Empty<CMUQueueBypassInfo>();

    public string EmptyText = string.Empty;

    public CMUQueueTopologyControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = cache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 10);
        MinHeight = 230;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void SetData(
        IReadOnlyList<CMUQueueNodeInfo> nodes,
        IReadOnlyList<CMUQueueRouteInfo> routes,
        IReadOnlyList<CMUQueueBypassInfo> bypasses)
    {
        _nodes = nodes;
        _routes = routes;
        _bypasses = bypasses;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawRect(box, CrtTerminalPalette.Surface0);

        if (_nodes.Count == 0)
        {
            handle.DrawString(
                _font,
                new Vector2(box.Left + 10, box.Top + 10),
                EmptyText,
                CrtTerminalPalette.TextDim);
            return;
        }

        var maxColumn = Math.Max(1, _nodes.Max(node => node.Column));
        var maxRow = Math.Max(1, _nodes.Max(node => node.Row));
        var left = box.Left + 68;
        var right = box.Right - 68;
        var top = box.Top + 18;
        var bottom = box.Bottom - 18;
        var cellWidth = (right - left) / maxColumn;
        var cellHeight = (bottom - top) / maxRow;
        var nodeWidth = Math.Min(112, Math.Max(72, cellWidth * 0.72f));
        var nodeHeight = Math.Min(46, Math.Max(34, cellHeight * 0.55f));

        var boxes = new Dictionary<string, UIBox2>(StringComparer.Ordinal);
        foreach (var node in _nodes)
        {
            var centerX = left + cellWidth * node.Column + cellWidth / 2;
            var centerY = top + cellHeight * node.Row + cellHeight / 2;
            boxes[node.Id] = new UIBox2(
                centerX - nodeWidth / 2,
                centerY - nodeHeight / 2,
                centerX + nodeWidth / 2,
                centerY + nodeHeight / 2);
        }

        var sourceRect = new UIBox2(box.Left + 8, (box.Top + box.Bottom) / 2 - 20, box.Left + 58, (box.Top + box.Bottom) / 2 + 20);
        var sinkRect = new UIBox2(box.Right - 58, (box.Top + box.Bottom) / 2 - 20, box.Right - 8, (box.Top + box.Bottom) / 2 + 20);

        DrawBox(handle, sourceRect, CrtTerminalPalette.Surface2, CrtTerminalPalette.Line);
        DrawBox(handle, sinkRect, CrtTerminalPalette.Surface2, CrtTerminalPalette.Line);
        DrawCentered(handle, sourceRect, Loc.GetString("cmu-queue-topology-in"), CrtTerminalPalette.TextDim);
        DrawCentered(handle, sinkRect, Loc.GetString("cmu-queue-topology-out"), CrtTerminalPalette.TextDim);

        var totalExternal = _nodes.Sum(node => node.ExternalRate);
        DrawLabel(
            handle,
            new Vector2((sourceRect.Left + sourceRect.Right) / 2, sourceRect.Top - 7),
            totalExternal.ToString("0.#"),
            CrtTerminalPalette.Accent);

        foreach (var node in _nodes)
        {
            if (node.ExternalRate <= 0)
                continue;

            var target = boxes[node.Id];
            var start = EdgePoint(sourceRect, Center(target));
            var end = EdgePoint(target, Center(sourceRect));
            DrawArrow(handle, start, end, CrtTerminalPalette.Accent);
            DrawLabel(
                handle,
                (start + end) / 2,
                node.ExternalRate.ToString("0.#"),
                CrtTerminalPalette.Accent);
        }

        foreach (var bypass in _bypasses)
        {
            if (!boxes.TryGetValue(bypass.From, out var from)
                || !boxes.TryGetValue(bypass.To, out var to))
            {
                continue;
            }

            var start = new Vector2((from.Left + from.Right) / 2, from.Top);
            var end = new Vector2((to.Left + to.Right) / 2, to.Top);
            var bend = Math.Min(start.Y, end.Y) - 16;
            DrawDashed(handle, start, new Vector2(start.X, bend), CrtTerminalPalette.Caution);
            DrawDashed(handle, new Vector2(start.X, bend), new Vector2(end.X, bend), CrtTerminalPalette.Caution);
            DrawArrow(handle, new Vector2(end.X, bend), end, CrtTerminalPalette.Caution);

            var label = bypass.CurrentPercent > 0
                ? Loc.GetString(
                    "cmu-queue-topology-bypass-active",
                    ("percent", (bypass.CurrentPercent * 100).ToString("0")))
                : Loc.GetString("cmu-queue-topology-bypass");
            DrawLabel(handle, new Vector2((start.X + end.X) / 2, bend - 7), label, CrtTerminalPalette.Caution);
        }

        foreach (var route in _routes)
        {
            var isSink = string.IsNullOrEmpty(route.To) || route.To == "sink";
            if (isSink)
            {
                if (!boxes.TryGetValue(route.From, out var fromSink))
                    continue;

                var start = EdgePoint(fromSink, Center(sinkRect));
                var end = EdgePoint(sinkRect, Center(fromSink));
                DrawArrow(handle, start, end, CrtTerminalPalette.Line);
                continue;
            }

            if (!boxes.TryGetValue(route.From, out var from)
                || !boxes.TryGetValue(route.To, out var to))
            {
                continue;
            }

            if (to.Left <= from.Left)
            {
                var start = new Vector2((from.Left + from.Right) / 2, from.Top);
                var end = new Vector2((to.Left + to.Right) / 2, to.Top);
                var bend = Math.Min(start.Y, end.Y) - 12;
                handle.DrawLine(start, new Vector2(start.X, bend), CrtTerminalPalette.Line);
                handle.DrawLine(new Vector2(start.X, bend), new Vector2(end.X, bend), CrtTerminalPalette.Line);
                DrawArrow(handle, new Vector2(end.X, bend), end, CrtTerminalPalette.Line);
            }
            else
            {
                var start = EdgePoint(from, Center(to));
                var end = EdgePoint(to, Center(from));
                DrawArrow(handle, start, end, CrtTerminalPalette.Line);

                if (route.Probability < 0.999)
                {
                    DrawLabel(
                        handle,
                        (start + end) / 2,
                        Loc.GetString(
                            "cmu-queue-topology-percent",
                            ("percent", (route.Probability * 100).ToString("0"))),
                        CrtTerminalPalette.TextDim);
                }
            }
        }

        foreach (var node in _nodes)
        {
            var rect = boxes[node.Id];
            var border = !node.Analyzed
                ? CrtTerminalPalette.Line
                : node.Stable
                    ? CrtTerminalPalette.Accent
                    : CrtTerminalPalette.Alert;
            DrawBox(handle, rect, CrtTerminalPalette.Surface2, border);
            DrawCentered(handle, rect, ShortName(node), CrtTerminalPalette.TextBright);

            var queue = Loc.GetString(
                "cmu-queue-topology-queue",
                ("queue", node.Queue.ToString("0.#")));
            handle.DrawString(
                _font,
                new Vector2(rect.Left + 4, rect.Bottom - 12),
                queue,
                CrtTerminalPalette.TextDim);
        }
    }

    private static void DrawBox(
        DrawingHandleScreen handle,
        UIBox2 rect,
        Color fill,
        Color border)
    {
        handle.DrawRect(rect, fill);
        handle.DrawLine(new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Top), border);
        handle.DrawLine(new Vector2(rect.Right, rect.Top), new Vector2(rect.Right, rect.Bottom), border);
        handle.DrawLine(new Vector2(rect.Right, rect.Bottom), new Vector2(rect.Left, rect.Bottom), border);
        handle.DrawLine(new Vector2(rect.Left, rect.Bottom), new Vector2(rect.Left, rect.Top), border);
    }

    private void DrawCentered(
        DrawingHandleScreen handle,
        UIBox2 rect,
        string text,
        Color color)
    {
        var width = text.Length * 5.5f;
        handle.DrawString(
            _font,
            new Vector2((rect.Left + rect.Right) / 2 - width / 2, (rect.Top + rect.Bottom) / 2 - 6),
            text,
            color);
    }

    private void DrawLabel(
        DrawingHandleScreen handle,
        Vector2 position,
        string text,
        Color color)
    {
        var width = text.Length * 5.5f;
        handle.DrawRect(
            new UIBox2(position.X - width / 2 - 2, position.Y - 7, position.X + width / 2 + 2, position.Y + 5),
            CrtTerminalPalette.Surface0);
        handle.DrawString(
            _font,
            new Vector2(position.X - width / 2, position.Y - 6),
            text,
            color);
    }

    private static void DrawArrow(
        DrawingHandleScreen handle,
        Vector2 start,
        Vector2 end,
        Color color)
    {
        handle.DrawLine(start, end, color);

        var direction = end - start;
        if (direction.LengthSquared() < 0.01f)
            return;

        direction = Vector2.Normalize(direction);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var tip = end;
        var left = end - direction * 7 + perpendicular * 3.5f;
        var right = end - direction * 7 - perpendicular * 3.5f;
        handle.DrawLine(tip, left, color);
        handle.DrawLine(tip, right, color);
    }

    private static void DrawDashed(
        DrawingHandleScreen handle,
        Vector2 start,
        Vector2 end,
        Color color)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length < 0.01f)
            return;

        var direction = delta / length;
        for (var offset = 0f; offset < length; offset += 8f)
        {
            var segmentEnd = MathF.Min(offset + 4f, length);
            handle.DrawLine(start + direction * offset, start + direction * segmentEnd, color);
        }
    }

    private static Vector2 Center(UIBox2 rect)
    {
        return new Vector2((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
    }

    private static Vector2 EdgePoint(UIBox2 rect, Vector2 toward)
    {
        var center = Center(rect);
        var delta = toward - center;
        if (delta.LengthSquared() < 0.01f)
            return center;

        var halfWidth = (rect.Right - rect.Left) / 2;
        var halfHeight = (rect.Bottom - rect.Top) / 2;
        var scaleX = MathF.Abs(delta.X) > 0.001f ? halfWidth / MathF.Abs(delta.X) : float.MaxValue;
        var scaleY = MathF.Abs(delta.Y) > 0.001f ? halfHeight / MathF.Abs(delta.Y) : float.MaxValue;
        return center + delta * MathF.Min(scaleX, scaleY);
    }

    private static string ShortName(CMUQueueNodeInfo node)
    {
        var key = $"{node.Name}-short";
        if (Loc.TryGetString(key, out var shortName))
            return string.IsNullOrEmpty(node.Suffix) ? shortName : $"{shortName} {node.Suffix}";

        var name = Loc.TryGetString(node.Name, out var fullName) ? fullName : node.Id;
        return string.IsNullOrEmpty(node.Suffix) ? name : $"{name} {node.Suffix}";
    }
}
