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
public sealed partial class CMURoboticsOrderConsoleWindow : DefaultWindow
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
