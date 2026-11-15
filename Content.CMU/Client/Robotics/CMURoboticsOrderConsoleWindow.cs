using System;
using System.Linq;
using System.Numerics;
using Content.Client.CMU14.Interface;
using Content.Client.CMU14.Robotics.QueueNetwork;
using Content.Shared.CMU14.Robotics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.Robotics;

/// <summary>
/// CRT-style robotics terminal: work orders, the production-network contract
/// tab, and the robotics-point recipe cartridge shop.
/// </summary>
public sealed class CMURoboticsOrderConsoleWindow : DefaultWindow
{
    public event Action<int>? OnAccept;
    public event Action? OnCancel;
    public event Action? OnPrintSlip;
    public event Action? OnRefresh;
    public event Action<string>? OnBuyCartridge;

    public CMUQueueNetworkConsolePanel QueuePanel { get; } = new();

    private readonly CrtConsoleKit _kit = new();
    private readonly CrtConsoleKit _cartridgeKit = new();
    private readonly TabContainer _tabs = new();
    private readonly BoxContainer _body;
    private readonly RichTextLabel _status;
    private readonly Label _subheading;
    private readonly BoxContainer _cartridgeBody;
    private readonly RichTextLabel _cartridgeStatus;
    private readonly Label _roboticsPoints;

    private string _cartridgeSignature = string.Empty;

    public CMURoboticsOrderConsoleWindow()
    {
        Title = Loc.GetString("cmu-robotics-console-title");
        MinSize = new Vector2(780, 620);
        SetSize = new Vector2(980, 800);

        _tabs.VerticalExpand = true;
        Contents.AddChild(_tabs);

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

        _tabs.AddChild(_kit.Tube);
        _tabs.SetTabTitle(0, Loc.GetString("cmu-robotics-tab-orders"));

        _tabs.AddChild(QueuePanel);
        _tabs.SetTabTitle(1, Loc.GetString("cmu-robotics-tab-network"));

        var cartridgeHeader = _cartridgeKit.Header(
            Loc.GetString("cmu-robotics-cartridges-heading"),
            out _);
        _cartridgeKit.Root.AddChild(cartridgeHeader);
        _cartridgeKit.Root.AddChild(_cartridgeKit.Readout(
            Loc.GetString("cmu-robotics-cartridges-robotics-points"),
            "0",
            out _roboticsPoints,
            CrtTerminalPalette.Accent));

        var cartridgeScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _cartridgeBody = CrtConsoleKit.Column(8);
        _cartridgeBody.HorizontalExpand = true;
        _cartridgeBody.Margin = new Thickness(0, 0, 6, 0);
        cartridgeScroll.AddChild(_cartridgeBody);
        _cartridgeKit.Root.AddChild(cartridgeScroll);

        var cartridgeFooter = CrtConsoleKit.Strip(CrtTerminalPalette.Surface1);
        _cartridgeStatus = CrtConsoleKit.Wrapping(string.Empty, CrtTerminalPalette.TextDim);
        cartridgeFooter.AddChild(_cartridgeStatus);
        _cartridgeKit.Root.AddChild(cartridgeFooter);

        _tabs.AddChild(_cartridgeKit.Tube);
        _tabs.SetTabTitle(2, Loc.GetString("cmu-robotics-tab-cartridges"));
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
            Loc.GetString("cmu-robotics-cartridges-robotics-points"),
            state.RoboticsPoints.ToString(),
            CrtTerminalPalette.Accent));

        _body.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-penalty"),
            state.LastPenalty > 0
                ? Loc.GetString(
                    "cmu-robotics-console-penalty-value",
                    ("penalty", CrtConsoleKit.Money(state.LastPenalty)),
                    ("missing", state.LastMissingElements))
                : Loc.GetString("cmu-robotics-console-penalty-none"),
            state.LastPenalty > 0 ? CrtTerminalPalette.Caution : CrtTerminalPalette.TextDim));

        _body.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-cooldown"),
            state.CooldownSeconds > 0
                ? Loc.GetString(
                    "cmu-robotics-console-cooldown-value",
                    ("seconds", state.CooldownSeconds))
                : Loc.GetString("cmu-robotics-console-cooldown-ready"),
            state.CooldownSeconds > 0 ? CrtTerminalPalette.Caution : CrtTerminalPalette.Accent));

        BuildActive(state.Active);
        BuildAvailable(state);

        if (state.Queue != null)
            QueuePanel.UpdateState(state.Queue);

        _roboticsPoints.Text = state.RoboticsPoints.ToString();
        SetRichText(
            _cartridgeStatus,
            state.CartridgeStatus,
            CrtTerminalPalette.TextDim);

        var signature = $"{state.RoboticsPoints}|{state.CartridgeStatus}|" +
                        string.Join(',', state.Cartridges.Select(cartridge =>
                            $"{cartridge.Prototype.Id}:{cartridge.Cost}:{cartridge.Description}"));
        if (signature == _cartridgeSignature)
            return;

        _cartridgeSignature = signature;
        BuildCartridges(state);
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
                accept.Disabled = state.Active != null || state.CooldownSeconds > 0;
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

    private void BuildCartridges(CMURoboticsOrderConsoleState state)
    {
        _cartridgeBody.RemoveAllChildren();

        if (state.Cartridges.Count == 0)
        {
            _cartridgeBody.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-robotics-cartridges-empty"),
                CrtTerminalPalette.TextDim));
            return;
        }

        foreach (var cartridge in state.Cartridges)
        {
            var id = cartridge.Prototype.Id;
            var name = GetPrototypeName(cartridge.Prototype);
            var section = _cartridgeKit.Section(name, out var column);

            if (!string.IsNullOrEmpty(cartridge.Description) &&
                Loc.TryGetString(cartridge.Description, out var description))
            {
                column.AddChild(CrtConsoleKit.Wrapping(
                    description,
                    CrtTerminalPalette.TextDim));
            }

            var affordable = state.RoboticsPoints >= cartridge.Cost;
            column.AddChild(_cartridgeKit.Field(
                Loc.GetString("cmu-robotics-cartridges-cost"),
                Loc.GetString(
                    "cmu-robotics-cartridges-cost-value",
                    ("cost", cartridge.Cost)),
                affordable ? CrtTerminalPalette.Accent : CrtTerminalPalette.Alert));

            var buy = _cartridgeKit.Button(
                Loc.GetString("cmu-robotics-cartridges-buy"),
                CmuButtonStyles.Variant.Affirm);
            buy.Disabled = !affordable;
            buy.OnPressed += _ => OnBuyCartridge?.Invoke(id);
            column.AddChild(buy);

            _cartridgeBody.AddChild(section);
        }
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

    private static void SetRichText(RichTextLabel label, string text, Color color)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        label.SetMessage(message);
    }
}
