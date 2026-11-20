using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.CMU14.Interface;
using Content.Client.Resources;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Client-side view state for the network schematic. It survives UI state
/// updates so moving nodes to declutter the drawing is not lost after every
/// analysis or upgrade.
/// </summary>
public sealed class CMUQueueTopologyView
{
    /// <summary>Per-node offsets in schematic pixels, keyed by node id.</summary>
    public Dictionary<string, Vector2> Offsets { get; } = new(StringComparer.Ordinal);

    /// <summary>Schematic pan in control pixels.</summary>
    public Vector2 Pan;

    /// <summary>Zoom factor applied around the control origin.</summary>
    public float Zoom = 1f;

    /// <summary>Restores the auto-layout positions, pan and zoom.</summary>
    public void Reset()
    {
        Offsets.Clear();
        Pan = Vector2.Zero;
        Zoom = 1f;
    }
}

/// <summary>
/// Draws the generated queueing network: external supply, nodes, routing
/// arrows, the sink and available intake diversions. Nodes can be dragged,
/// the background can be panned and the wheel zooms the schematic.
/// </summary>
public sealed class CMUQueueTopologyControl : Control
{
    private const float MinZoom = 0.85f;
    private const float MaxZoom = 2f;

    private readonly Font _font;
    private readonly ILocalizationManager _loc = IoCManager.Resolve<ILocalizationManager>();
    private IReadOnlyList<CMUQueueNodeInfo> _nodes = Array.Empty<CMUQueueNodeInfo>();
    private IReadOnlyList<CMUQueueRouteInfo> _routes = Array.Empty<CMUQueueRouteInfo>();
    private IReadOnlyList<CMUQueueBypassInfo> _bypasses = Array.Empty<CMUQueueBypassInfo>();

    private string? _draggingNode;
    private Vector2 _dragStartWorld;
    private Vector2 _dragStartOffset;
    private bool _panning;
    private Vector2 _panStartScreen;
    private Vector2 _panStart;

    /// <summary>
    /// Client-side view state shared by rebuilt topology controls. It keeps
    /// manual node positions, pan and zoom for the current contract.
    /// </summary>
    public CMUQueueTopologyView View { get; set; } = new();

    /// <summary>Localized message shown when the network has no nodes.</summary>
    public string EmptyText = string.Empty;

    public CMUQueueTopologyControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = cache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 10);
        MinHeight = 300;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
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

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            View.Reset();
            _draggingNode = null;
            _panning = false;
            args.Handle();
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (_nodes.Count == 0)
            return;

        var mouse = LocalMouse(args.RelativePixelPosition);
        var world = ScreenToWorld(mouse);
        BuildWorldLayout(out var boxes, out _, out _);

        foreach (var node in _nodes)
        {
            if (!boxes.TryGetValue(node.Id, out var rect) || !Contains(rect, world))
                continue;

            _draggingNode = node.Id;
            _dragStartWorld = world;
            _dragStartOffset = View.Offsets.GetValueOrDefault(node.Id);
            _panning = false;
            args.Handle();
            return;
        }

        _panning = true;
        _panStartScreen = mouse;
        _panStart = View.Pan;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _draggingNode = null;
        _panning = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var mouse = LocalMouse(args.RelativePixelPosition);
        if (_draggingNode != null)
        {
            var world = ScreenToWorld(mouse);
            View.Offsets[_draggingNode] = _dragStartOffset + world - _dragStartWorld;
            return;
        }

        if (_panning)
            View.Pan = _panStart + mouse - _panStartScreen;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _draggingNode = null;
        _panning = false;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        var delta = args.Delta.Y;
        if (MathF.Abs(delta) < 0.01f)
            return;

        var mouse = LocalMouse(args.RelativePixelPosition);
        var oldZoom = View.Zoom;
        var factor = delta > 0 ? 1.15f : 1 / 1.15f;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (MathF.Abs(newZoom - oldZoom) < 0.001f)
            return;

        var world = (mouse - View.Pan) / oldZoom;
        View.Zoom = newZoom;
        View.Pan = mouse - world * newZoom;
        args.Handle();
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

        BuildWorldLayout(out var worldBoxes, out var worldSource, out var worldSink);
        var boxes = new Dictionary<string, UIBox2>(StringComparer.Ordinal);
        foreach (var (id, rect) in worldBoxes)
            boxes[id] = MapBox(rect);

        var sourceRect = MapBox(worldSource);
        var sinkRect = MapBox(worldSink);

        DrawBox(handle, sourceRect, CrtTerminalPalette.Surface2, CrtTerminalPalette.Line);
        DrawBox(handle, sinkRect, CrtTerminalPalette.Surface2, CrtTerminalPalette.Line);
        DrawCentered(handle, sourceRect, Loc.GetString("cmu-queue-topology-in"), CrtTerminalPalette.TextDim);
        DrawCentered(handle, sinkRect, Loc.GetString("cmu-queue-topology-out"), CrtTerminalPalette.TextDim);

        foreach (var node in _nodes)
        {
            if (node.ExternalRate <= 0)
                continue;

            var target = boxes[node.Id];
            var start = EdgePoint(sourceRect, Center(target));
            var end = EdgePoint(target, Center(sourceRect));
            DrawArrow(handle, start, end, CrtTerminalPalette.Accent);
            var direction = Vector2.Normalize(end - start);
            var perpendicular = new Vector2(-direction.Y, direction.X);
            DrawLabel(
                handle,
                (start + end) / 2 + perpendicular * 9,
                Loc.GetString(
                    "cmu-queue-topology-external",
                    ("rate", node.ExternalRate.ToString("0.#"))),
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
            var fromCenter = Center(from);
            var toCenter = Center(to);
            var targetBelow = toCenter.Y > fromCenter.Y + (from.Bottom - from.Top) / 2;
            if (targetBelow)
            {
                start = new Vector2(fromCenter.X, from.Bottom);
                end = new Vector2(toCenter.X, to.Bottom);
            }

            var bend = targetBelow
                ? Math.Max(start.Y, end.Y) + 18
                : Math.Min(start.Y, end.Y) - 18;
            DrawDashed(handle, start, new Vector2(start.X, bend), CrtTerminalPalette.Caution);
            DrawDashed(handle, new Vector2(start.X, bend), new Vector2(end.X, bend), CrtTerminalPalette.Caution);
            DrawArrow(handle, new Vector2(end.X, bend), end, CrtTerminalPalette.Caution);

            var label = bypass.CurrentPercent > 0
                ? Loc.GetString(
                    "cmu-queue-topology-bypass-active",
                    ("percent", (bypass.CurrentPercent * 100).ToString("0")))
                : Loc.GetString("cmu-queue-topology-bypass");
            DrawLabel(
                handle,
                new Vector2((start.X + end.X) / 2, targetBelow ? bend + 11 : bend - 11),
                label,
                CrtTerminalPalette.Caution);
        }

        foreach (var route in _routes)
        {
            var isSink = string.IsNullOrEmpty(route.To) || route.To == "sink";
            if (isSink)
            {
                if (!boxes.TryGetValue(route.From, out var fromSink))
                    continue;

                var fromCenter = Center(fromSink);
                var sinkEnd = new Vector2(
                    sinkRect.Left,
                    Math.Clamp(fromCenter.Y, sinkRect.Top + 4, sinkRect.Bottom - 4));
                var start = EdgePoint(fromSink, sinkEnd);
                DrawArrow(handle, start, sinkEnd, CrtTerminalPalette.Line);
                continue;
            }

            if (!boxes.TryGetValue(route.From, out var from)
                || !boxes.TryGetValue(route.To, out var to))
            {
                continue;
            }

            if (to.Left <= from.Left)
            {
                var fromCenter = Center(from);
                var toCenter = Center(to);
                var targetBelow = toCenter.Y > fromCenter.Y;
                var startY = targetBelow ? from.Bottom : from.Top;
                var endY = targetBelow ? to.Bottom : to.Top;
                var start = new Vector2(fromCenter.X, startY);
                var end = new Vector2(toCenter.X, endY);
                var bend = targetBelow
                    ? Math.Max(startY, endY) + 14
                    : Math.Min(startY, endY) - 14;
                handle.DrawLine(start, new Vector2(start.X, bend), CrtTerminalPalette.Line);
                handle.DrawLine(new Vector2(start.X, bend), new Vector2(end.X, bend), CrtTerminalPalette.Line);
                DrawArrow(handle, new Vector2(end.X, bend), end, CrtTerminalPalette.Line);

                if (route.Probability < 0.999)
                {
                    DrawLabel(
                        handle,
                        new Vector2((start.X + end.X) / 2, targetBelow ? bend + 9 : bend - 9),
                        Loc.GetString(
                            "cmu-queue-topology-percent",
                            ("percent", (route.Probability * 100).ToString("0"))),
                        CrtTerminalPalette.TextDim);
                }
            }
            else
            {
                var start = EdgePoint(from, Center(to));
                var end = EdgePoint(to, Center(from));
                DrawArrow(handle, start, end, CrtTerminalPalette.Line);

                if (route.Probability < 0.999)
                {
                    var direction = Vector2.Normalize(end - start);
                    var perpendicular = new Vector2(-direction.Y, direction.X);
                    DrawLabel(
                        handle,
                        (start + end) / 2 + perpendicular * 13,
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
            var border = node.Analyzed
                ? CrtTerminalPalette.Accent
                : CrtTerminalPalette.Line;
            DrawBox(handle, rect, CrtTerminalPalette.Surface2, border);
            DrawCentered(handle, rect, ShortName(node), CrtTerminalPalette.TextBright);

            if (node.Queue > 0.05)
            {
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
    }

    private void BuildWorldLayout(
        out Dictionary<string, UIBox2> boxes,
        out UIBox2 sourceRect,
        out UIBox2 sinkRect)
    {
        var box = PixelSizeBox;
        var columns = _nodes.Max(node => node.Column) + 1;
        var rows = _nodes.Max(node => node.Row) + 1;
        var left = box.Left + 76;
        var right = box.Right - 76;
        var top = box.Top + 22;
        var bottom = box.Bottom - 28;
        var cellWidth = Math.Max(1, (right - left) / columns);
        var cellHeight = Math.Max(1, (bottom - top) / rows);
        var nodeWidth = Math.Min(118, Math.Max(64, cellWidth * 0.78f));
        var nodeHeight = Math.Min(48, Math.Max(40, cellHeight * 0.62f));

        boxes = new Dictionary<string, UIBox2>(StringComparer.Ordinal);
        foreach (var node in _nodes)
        {
            var offset = View.Offsets.GetValueOrDefault(node.Id);
            var centerX = left + cellWidth * node.Column + cellWidth / 2 + offset.X;
            var centerY = top + cellHeight * node.Row + cellHeight / 2 + offset.Y;
            boxes[node.Id] = new UIBox2(
                centerX - nodeWidth / 2,
                centerY - nodeHeight / 2,
                centerX + nodeWidth / 2,
                centerY + nodeHeight / 2);
        }

        var center = (box.Top + box.Bottom) / 2;
        sourceRect = new UIBox2(box.Left + 8, center - 20, box.Left + 66, center + 20);
        sinkRect = new UIBox2(box.Right - 66, center - 20, box.Right - 8, center + 20);
    }

    private Vector2 WorldToScreen(Vector2 point)
    {
        return point * View.Zoom + View.Pan;
    }

    private Vector2 ScreenToWorld(Vector2 point)
    {
        return (point - View.Pan) / View.Zoom;
    }

    private UIBox2 MapBox(UIBox2 box)
    {
        return new UIBox2(
            WorldToScreen(new Vector2(box.Left, box.Top)),
            WorldToScreen(new Vector2(box.Right, box.Bottom)));
    }

    private Vector2 LocalMouse(Vector2 relativePixelPosition)
    {
        var box = PixelSizeBox;
        return new Vector2(box.Left + relativePixelPosition.X, box.Top + relativePixelPosition.Y);
    }

    private static bool Contains(UIBox2 rect, Vector2 point)
    {
        return point.X >= rect.Left
            && point.X <= rect.Right
            && point.Y >= rect.Top
            && point.Y <= rect.Bottom;
    }

    private float MeasureText(string text)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
            width += _font.GetCharMetrics(rune, 1f)?.Advance ?? 0f;

        return width;
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
        var width = MeasureText(text);
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
        var width = MeasureText(text);
        handle.DrawRect(
            new UIBox2(position.X - width / 2 - 3, position.Y - 8, position.X + width / 2 + 3, position.Y + 5),
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

    private string ShortName(CMUQueueNodeInfo node)
    {
        var key = $"{node.Name}-short";
        if (_loc.TryGetString(key, out var shortName))
            return string.IsNullOrEmpty(node.Suffix) ? shortName : $"{shortName} {node.Suffix}";

        var name = _loc.TryGetString(node.Name, out var fullName) ? fullName : node.Id;
        return string.IsNullOrEmpty(node.Suffix) ? name : $"{name} {node.Suffix}";
    }
}
