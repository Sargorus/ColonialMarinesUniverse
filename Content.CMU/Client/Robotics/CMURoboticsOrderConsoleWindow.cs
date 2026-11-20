using System;
using System.Numerics;
using Content.Client.CMU14.Interface;
using Content.Shared.CMU14.Robotics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.Robotics;

/// <summary>
/// CRT-style list of available robotics orders and the currently accepted job.
/// </summary>
public sealed class CMURoboticsOrderConsoleWindow : DefaultWindow
{
    public event Action<int>? OnAccept;
    public event Action? OnCancel;
    public event Action? OnPrintSlip;
    public event Action? OnRefresh;

    private readonly CrtConsoleKit _kit = new();
    private readonly BoxContainer _body;
    private readonly RichTextLabel _status;
    private readonly Label _subheading;

    public CMURoboticsOrderConsoleWindow()
    {
        Title = Loc.GetString("cmu-robotics-console-title");
        MinSize = new Vector2(460, 500);
        SetSize = new Vector2(560, 640);

        var header = _kit.Header(Loc.GetString("cmu-robotics-console-heading"), out _subheading);
        _kit.Root.AddChild(header);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _body = CrtConsoleKit.Column(8);
        _body.HorizontalExpand = true;
        _body.Margin = new Thickness(0, 0, 6, 0);
        scroll.AddChild(_body);
        _kit.Root.AddChild(scroll);

        var footer = CrtConsoleKit.Strip(CrtTerminalPalette.Surface1);
        _status = CrtConsoleKit.Wrapping(string.Empty, CrtTerminalPalette.TextDim);
        footer.AddChild(_status);
        _kit.Root.AddChild(footer);

        Contents.AddChild(_kit.Tube);
    }

    public void UpdateState(CMURoboticsOrderConsoleState state)
    {
        _subheading.Text = Loc.GetString("cmu-robotics-console-subheading");

        var status = new FormattedMessage();
        status.PushColor(CrtTerminalPalette.TextDim);
        status.AddText(state.Status);
        status.Pop();
        _status.SetMessage(status);

        _body.RemoveAllChildren();

        _body.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-penalty"),
            state.LastPenalty > 0
                ? Loc.GetString(
                    "cmu-robotics-console-penalty-value",
                    ("penalty", CrtConsoleKit.Money(state.LastPenalty)),
                    ("missing", state.LastMissingElements))
                : Loc.GetString("cmu-robotics-console-penalty-none"),
            state.LastPenalty > 0 ? CrtTerminalPalette.Caution : CrtTerminalPalette.TextDim));

        BuildActive(state.Active);
        BuildAvailable(state);
    }

    private void BuildActive(CMURoboticsOrderInfo? active)
    {
        var section = _kit.Section(Loc.GetString("cmu-robotics-console-active"), out var column);

        if (active is not { } order)
        {
            column.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-robotics-console-no-active"),
                CrtTerminalPalette.TextDim));
            _body.AddChild(section);
            return;
        }

        AddOrderFields(column, order);

        var print = _kit.Button(
            Loc.GetString("cmu-robotics-console-print-slip"),
            CmuButtonStyles.Variant.Neutral);
        print.OnPressed += _ => OnPrintSlip?.Invoke();
        column.AddChild(print);

        var cancel = _kit.Button(
            Loc.GetString("cmu-robotics-console-cancel"),
            CmuButtonStyles.Variant.Caution);
        cancel.OnPressed += _ => OnCancel?.Invoke();
        column.AddChild(cancel);

        var hint = order.Type == CMURoboticsOrderType.Repair
            ? Loc.GetString(
                "cmu-robotics-console-delivery-hint-repair",
                ("amount", order.Amount))
            : Loc.GetString("cmu-robotics-console-delivery-hint-build");
        column.AddChild(CrtConsoleKit.Wrapping(hint, CrtTerminalPalette.TextDim));
        _body.AddChild(section);
    }

    private void BuildAvailable(CMURoboticsOrderConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-robotics-console-available"), out var column);

        if (state.Available.Count == 0)
        {
            column.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-robotics-console-no-orders"),
                CrtTerminalPalette.TextDim));
        }
        else
        {
            foreach (var order in state.Available)
            {
                var card = _kit.Section(
                    Loc.GetString(
                        "cmu-robotics-console-order-heading",
                        ("type", TypeName(order.Type)),
                        ("target", Loc.GetString(order.TargetNameLocId))),
                    out var details);

                AddOrderFields(details, order);

                var accept = _kit.Button(
                    Loc.GetString("cmu-robotics-console-accept"),
                    CmuButtonStyles.Variant.Affirm);
                accept.Disabled = state.Active != null;
                var id = order.Id;
                accept.OnPressed += _ => OnAccept?.Invoke(id);
                details.AddChild(accept);

                column.AddChild(card);
            }
        }

        var refresh = _kit.Button(
            Loc.GetString("cmu-robotics-console-refresh"),
            CmuButtonStyles.Variant.Neutral);
        refresh.Disabled = state.Active != null;
        refresh.OnPressed += _ => OnRefresh?.Invoke();
        column.AddChild(refresh);

        _body.AddChild(section);
    }

    private void AddOrderFields(BoxContainer column, CMURoboticsOrderInfo order)
    {
        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-type"),
            TypeName(order.Type)));

        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-target"),
            Loc.GetString(order.TargetNameLocId)));

        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-amount"),
            order.Amount.ToString()));

        if (order.RequiredModule is { } module)
        {
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-robotics-console-module"),
                GetPrototypeName(module)));
        }

        if (order.SecondsRemaining > 0)
        {
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-robotics-console-time-left"),
                TimeSpan.FromSeconds(order.SecondsRemaining).ToString(@"mm\:ss")));
        }

        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-reward"),
            CrtConsoleKit.Money(order.Reward),
            CrtTerminalPalette.Accent));
    }

    private static string TypeName(CMURoboticsOrderType type)
    {
        return Loc.GetString(type == CMURoboticsOrderType.Build
            ? "cmu-robotics-console-type-build"
            : "cmu-robotics-console-type-repair");
    }

    private static string GetPrototypeName(EntProtoId prototype)
    {
        return Loc.TryGetString($"ent-{prototype.Id}", out var name)
            ? name
            : prototype.Id;
    }
}
