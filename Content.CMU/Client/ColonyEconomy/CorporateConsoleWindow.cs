using System;
using System.Numerics;
using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Content.Shared.CMU14.ColonyEconomy;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.ColonyEconomy;

/// <summary>
///     The corporate affairs console, in the CRT theme. The liaison's operations desk, the science account,
///     the company mission board, the WEYU PMC call-in and the complex's main research direction live here.
/// </summary>
public sealed partial class CorporateConsoleWindow : DefaultWindow
{
    public event Action<int>? OnWithdraw;
    public event Action<float>? OnSetTariff;
    public event Action? OnOpenThirdParty;
    public event Action<string>? OnAcceptMission;
    public event Action<CorporateResearchDirection>? OnSetDirection;
    public event Action? OnCallPmc;
    public event Action? OnCallGuard;
    public event Action? OnRequestCredit;
    public event Action? OnRequestAlienTech;

    private readonly TabContainer _tabs = new();

    private readonly CrtConsoleKit _operationsKit = new();
    private readonly CrtConsoleKit _scienceKit = new();
    private readonly CrtConsoleKit _missionKit = new();
    private readonly CrtConsoleKit _pmcKit = new();
    private readonly CrtConsoleKit _directionKit = new();

    // Operations.
    private Label _subheading = null!;
    private PanelContainer _budgetTag = null!;
    private Label _budgetText = null!;
    private Label _taxNote = null!;
    private Label _tariff = null!;
    private LineEdit _tariffInput = null!;
    private BoxContainer _economy = null!;

    // Science.
    private Label _chemistryPoints = null!;
    private Label _roboticsPoints = null!;
    private Label _companyValue = null!;
    private Label _scienceLevel = null!;
    private Label _scienceDirection = null!;

    // Company tasks.
    private BoxContainer _missionBody = null!;
    private RichTextLabel _missionStatus = null!;
    private Label _missionChemistry = null!;
    private Label _missionRobotics = null!;
    private Label _missionCompanyValue = null!;
    private string _missionSignature = string.Empty;

    // Company support.
    private Label _pmcCompanyValue = null!;
    private Label _pmcCost = null!;
    private Label _pmcCalls = null!;
    private Label _pmcCooldown = null!;
    private ConfirmButton _pmcButton = null!;
    private Label _guardCost = null!;
    private Label _guardCalls = null!;
    private Label _guardCooldown = null!;
    private ConfirmButton _guardButton = null!;
    private Label _creditCost = null!;
    private Label _creditAmount = null!;
    private ConfirmButton _creditButton = null!;
    private Label _alienTechCostValueLabel = null!;
    private ConfirmButton _alienTechButton = null!;
    private RichTextLabel _supportStatus = null!;
    private int _pmcCostValue;
    private int _pmcCallsLeft;
    private int _pmcCooldownSeconds;
    private int _guardCostValue;
    private int _guardCallsLeft;
    private int _guardCooldownSeconds;
    private int _creditCostValue;
    private int _alienTechCostValue;

    // Direction.
    private Label _directionCurrent = null!;
    private ConfirmButton _roboticsDirection = null!;
    private ConfirmButton _bioengineeringDirection = null!;

    public CorporateConsoleWindow()
    {
        Title = Loc.GetString("corporate-console-title");
        MinSize = new Vector2(620, 580);
        SetSize = new Vector2(700, 720);

        _tabs.VerticalExpand = true;
        Contents.AddChild(_tabs);

        BuildOperationsTab();
        _tabs.AddChild(_operationsKit.Tube);
        _tabs.SetTabTitle(0, Loc.GetString("cmu-corporate-tab-operations"));

        BuildScienceTab();
        _tabs.AddChild(_scienceKit.Tube);
        _tabs.SetTabTitle(1, Loc.GetString("cmu-corporate-tab-science"));

        BuildMissionTab();
        _tabs.AddChild(_missionKit.Tube);
        _tabs.SetTabTitle(2, Loc.GetString("cmu-corporate-tab-missions"));

        BuildPmcTab();
        _tabs.AddChild(_pmcKit.Tube);
        _tabs.SetTabTitle(3, Loc.GetString("cmu-corporate-tab-pmc"));

        BuildDirectionTab();
        _tabs.AddChild(_directionKit.Tube);
        _tabs.SetTabTitle(4, Loc.GetString("cmu-corporate-tab-direction"));
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
