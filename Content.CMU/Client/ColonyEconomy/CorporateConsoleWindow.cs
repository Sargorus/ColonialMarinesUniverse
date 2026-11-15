using System;
using System.Linq;
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
public sealed class CorporateConsoleWindow : DefaultWindow
{
    /// <summary>The highest tariff the console accepts.</summary>
    private const int MaxTariff = 50;

    private static readonly int[] TariffPresets = [0, 10, 20, 30, 40, 50];

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

    private void BuildOperationsTab()
    {
        _budgetTag = _operationsKit.Tag(out _budgetText);
        _operationsKit.Root.AddChild(_operationsKit.Header(
            Loc.GetString("cmu-corporate-heading"), out _subheading, _budgetTag));
        _subheading.Text = Loc.GetString("cmu-corporate-subheading");

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var body = CrtConsoleKit.Column(8);
        body.HorizontalExpand = true;
        body.Margin = new Thickness(0, 0, 6, 0);
        scroll.AddChild(body);
        _operationsKit.Root.AddChild(scroll);

        // Treasury.
        body.AddChild(_operationsKit.Section(Loc.GetString("cmu-corporate-treasury"), out var treasury));
        var withdraw = CrtConsoleKit.Row(4);
        foreach (var amount in new[] { 100, 250, 500 })
        {
            var button = _operationsKit.Button(Loc.GetString("cmu-ambassador-withdraw", ("amount", amount)));
            button.HorizontalExpand = true;
            button.OnPressed += _ => OnWithdraw?.Invoke(amount);
            withdraw.AddChild(button);
        }

        treasury.AddChild(withdraw);
        _taxNote = CrtConsoleKit.Text(string.Empty, _operationsKit.Small, CrtTerminalPalette.TextDim);
        treasury.AddChild(_taxNote);

        // Transit tariff: the current rate big, then presets and an exact figure.
        body.AddChild(_operationsKit.Section(Loc.GetString("cmu-corporate-tariff"), out var tariff));
        var tariffRow = CrtConsoleKit.Row();
        var current = _operationsKit.Readout(
            Loc.GetString("cmu-corporate-current-tariff"), string.Empty, out _tariff);
        current.HorizontalExpand = false;
        current.MinWidth = 140;
        _tariff.FontOverride = _operationsKit.Huge;
        tariffRow.AddChild(current);
        tariffRow.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("corporate-console-transit-tariff-description"), CrtTerminalPalette.TextDim));
        tariff.AddChild(tariffRow);

        var presets = new GridContainer
        {
            Columns = TariffPresets.Length,
            HSeparationOverride = 4,
            HorizontalExpand = true,
        };
        foreach (var preset in TariffPresets)
        {
            var button = _operationsKit.Button($"{preset}%");
            button.HorizontalExpand = true;
            button.OnPressed += _ => OnSetTariff?.Invoke(preset);
            presets.AddChild(button);
        }

        tariff.AddChild(presets);

        var exact = CrtConsoleKit.Row(4);
        var key = CrtConsoleKit.Text(
            Loc.GetString("cmu-corporate-exact", ("max", MaxTariff)),
            _operationsKit.Small,
            CrtTerminalPalette.TextDim);
        key.VerticalAlignment = VAlignment.Center;
        key.MinWidth = 150;
        exact.AddChild(key);
        _tariffInput = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("corporate-console-tariff-placeholder"),
        };
        _tariffInput.OnTextEntered += _ => ApplyTariff();
        exact.AddChild(_tariffInput);
        var apply = _operationsKit.Button(Loc.GetString("cmu-corporate-apply"), CmuButtonStyles.Variant.Caution);
        apply.MinWidth = 90;
        apply.OnPressed += _ => ApplyTariff();
        exact.AddChild(apply);
        tariff.AddChild(exact);
        tariff.AddChild(CrtConsoleKit.Text(
            Loc.GetString("cmu-corporate-tariff-announced"), _operationsKit.Small, CrtTerminalPalette.Caution));

        // Economy at a glance.
        body.AddChild(_operationsKit.Section(Loc.GetString("cmu-ambassador-economy"), out _economy));

        // Corporate support.
        var open = _operationsKit.Button(
            Loc.GetString("cmu-ambassador-open-third-party"), CmuButtonStyles.Variant.Affirm);
        open.OnPressed += _ => OnOpenThirdParty?.Invoke();
        body.AddChild(_operationsKit.Section(
            Loc.GetString("cmu-corporate-support"), out var support, right: open));
        support.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-support-hint"), CrtTerminalPalette.TextDim));

        _operationsKit.Root.AddChild(_operationsKit.Footer(out var footer));
        footer.Text = Loc.GetString("cmu-corporate-footer");
    }

    private void BuildScienceTab()
    {
        _scienceKit.Root.AddChild(_scienceKit.Header(
            Loc.GetString("cmu-corporate-science-heading"), out var subheading));
        subheading.Text = Loc.GetString("cmu-corporate-science-subheading");

        var readouts = new GridContainer
        {
            Columns = 2,
            HSeparationOverride = 6,
            VSeparationOverride = 6,
            HorizontalExpand = true,
        };
        readouts.AddChild(_scienceKit.Readout(
            Loc.GetString("cmu-corporate-science-chemistry"), "0", out _chemistryPoints,
            CrtTerminalPalette.Accent));
        readouts.AddChild(_scienceKit.Readout(
            Loc.GetString("cmu-corporate-science-robotics"), "0", out _roboticsPoints,
            CrtTerminalPalette.Caution));
        readouts.AddChild(_scienceKit.Readout(
            Loc.GetString("cmu-corporate-science-company-value"), "0", out _companyValue,
            CrtTerminalPalette.Accent));
        readouts.AddChild(_scienceKit.Readout(
            Loc.GetString("cmu-corporate-science-level"), "1", out _scienceLevel,
            CrtTerminalPalette.TextBright));
        _scienceKit.Root.AddChild(readouts);

        var directionRow = CrtConsoleKit.Row();
        var directionKey = CrtConsoleKit.Text(
            Loc.GetString("cmu-corporate-science-direction"), _scienceKit.Small, CrtTerminalPalette.TextDim);
        directionKey.MinWidth = 150;
        directionKey.VerticalAlignment = VAlignment.Center;
        directionRow.AddChild(directionKey);
        _scienceDirection = CrtConsoleKit.Text(
            Loc.GetString("cmu-company-direction-none"), _scienceKit.Bold, CrtTerminalPalette.TextBright);
        directionRow.AddChild(_scienceDirection);
        _scienceKit.Root.AddChild(_scienceKit.Section(
            Loc.GetString("cmu-corporate-science-account"), out var account));
        account.AddChild(directionRow);

        account.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-science-chemistry-hint"), CrtTerminalPalette.TextDim));
        account.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-science-robotics-hint"), CrtTerminalPalette.TextDim));
        account.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-science-company-value-hint"), CrtTerminalPalette.TextDim));

        _scienceKit.Root.AddChild(_scienceKit.Footer(out var footer));
        footer.Text = Loc.GetString("cmu-corporate-science-footer");
    }

    private void BuildMissionTab()
    {
        _missionKit.Root.AddChild(_missionKit.Header(
            Loc.GetString("cmu-corporate-missions-heading"), out var subheading));
        subheading.Text = Loc.GetString("cmu-corporate-missions-subheading");

        var readouts = new GridContainer
        {
            Columns = 3,
            HSeparationOverride = 6,
            HorizontalExpand = true,
        };
        readouts.AddChild(_missionKit.Readout(
            Loc.GetString("cmu-corporate-science-chemistry"), "0", out _missionChemistry,
            CrtTerminalPalette.Accent));
        readouts.AddChild(_missionKit.Readout(
            Loc.GetString("cmu-corporate-science-robotics"), "0", out _missionRobotics,
            CrtTerminalPalette.Caution));
        readouts.AddChild(_missionKit.Readout(
            Loc.GetString("cmu-corporate-science-company-value"), "0", out _missionCompanyValue,
            CrtTerminalPalette.Accent));
        _missionKit.Root.AddChild(readouts);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _missionBody = CrtConsoleKit.Column(8);
        _missionBody.HorizontalExpand = true;
        _missionBody.Margin = new Thickness(0, 0, 6, 0);
        scroll.AddChild(_missionBody);
        _missionKit.Root.AddChild(scroll);

        var footer = CrtConsoleKit.Strip(CrtTerminalPalette.Surface1);
        _missionStatus = CrtConsoleKit.Wrapping(string.Empty, CrtTerminalPalette.TextDim);
        footer.AddChild(_missionStatus);
        _missionKit.Root.AddChild(footer);
    }

    private void BuildPmcTab()
    {
        _pmcKit.Root.AddChild(_pmcKit.Header(
            Loc.GetString("cmu-corporate-pmc-heading"), out var subheading));
        subheading.Text = Loc.GetString("cmu-corporate-pmc-subheading");

        var readouts = new GridContainer
        {
            Columns = 3,
            HSeparationOverride = 6,
            HorizontalExpand = true,
        };
        readouts.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-science-company-value"), "0", out _pmcCompanyValue,
            CrtTerminalPalette.Accent));
        readouts.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-pmc-cost"), "0", out _pmcCost,
            CrtTerminalPalette.Caution));
        readouts.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-pmc-calls"), "0", out _pmcCalls,
            CrtTerminalPalette.TextBright));
        _pmcKit.Root.AddChild(readouts);

        _pmcCooldown = CrtConsoleKit.Text(string.Empty, _pmcKit.Small, CrtTerminalPalette.TextDim);
        _pmcKit.Root.AddChild(_pmcCooldown);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var body = CrtConsoleKit.Column(8);
        body.HorizontalExpand = true;
        body.Margin = new Thickness(0, 0, 6, 0);
        scroll.AddChild(body);
        _pmcKit.Root.AddChild(scroll);

        // PMC squad.
        body.AddChild(_pmcKit.Section(Loc.GetString("cmu-corporate-pmc-contract"), out var contract));
        contract.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-pmc-description"), CrtTerminalPalette.TextDim));

        _pmcButton = new ConfirmButton
        {
            Text = Loc.GetString("cmu-corporate-pmc-call"),
            ConfirmationText = Loc.GetString("cmu-corporate-pmc-confirm"),
            MinWidth = 260,
        };
        CmuButtonStyles.Apply(_pmcButton, CmuButtonStyles.Variant.Affirm);
        _pmcButton.Label.FontOverride = _pmcKit.Bold;
        _pmcButton.OnPressed += _ => OnCallPmc?.Invoke();
        contract.AddChild(_pmcButton);

        // Corporate guard squad.
        body.AddChild(_pmcKit.Section(Loc.GetString("cmu-corporate-guard-heading"), out var guard));
        guard.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-guard-description"), CrtTerminalPalette.TextDim));
        var guardReadouts = new GridContainer
        {
            Columns = 2,
            HSeparationOverride = 6,
            HorizontalExpand = true,
        };
        guardReadouts.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-guard-cost"), "0", out _guardCost,
            CrtTerminalPalette.Caution));
        guardReadouts.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-guard-calls"), "0", out _guardCalls,
            CrtTerminalPalette.TextBright));
        guard.AddChild(guardReadouts);

        _guardCooldown = CrtConsoleKit.Text(string.Empty, _pmcKit.Small, CrtTerminalPalette.TextDim);
        guard.AddChild(_guardCooldown);

        _guardButton = new ConfirmButton
        {
            Text = Loc.GetString("cmu-corporate-guard-call"),
            ConfirmationText = Loc.GetString("cmu-corporate-guard-confirm"),
            MinWidth = 260,
        };
        CmuButtonStyles.Apply(_guardButton, CmuButtonStyles.Variant.Affirm);
        _guardButton.Label.FontOverride = _pmcKit.Bold;
        _guardButton.OnPressed += _ => OnCallGuard?.Invoke();
        guard.AddChild(_guardButton);

        // Credit.
        body.AddChild(_pmcKit.Section(Loc.GetString("cmu-corporate-credit-heading"), out var credit));
        credit.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-credit-description"), CrtTerminalPalette.TextDim));
        var creditReadouts = new GridContainer
        {
            Columns = 2,
            HSeparationOverride = 6,
            HorizontalExpand = true,
        };
        creditReadouts.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-credit-cost"), "0", out _creditCost,
            CrtTerminalPalette.Caution));
        creditReadouts.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-credit-amount"), "0", out _creditAmount,
            CrtTerminalPalette.Accent));
        credit.AddChild(creditReadouts);

        _creditButton = new ConfirmButton
        {
            Text = Loc.GetString("cmu-corporate-credit-request"),
            ConfirmationText = Loc.GetString("cmu-corporate-credit-confirm"),
            MinWidth = 260,
        };
        CmuButtonStyles.Apply(_creditButton, CmuButtonStyles.Variant.Affirm);
        _creditButton.Label.FontOverride = _pmcKit.Bold;
        _creditButton.OnPressed += _ => OnRequestCredit?.Invoke();
        credit.AddChild(_creditButton);

        // Alien technology.
        body.AddChild(_pmcKit.Section(Loc.GetString("cmu-corporate-alien-tech-heading"), out var alienTech));
        alienTech.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-alien-tech-description"), CrtTerminalPalette.TextDim));
        alienTech.AddChild(_pmcKit.Readout(
            Loc.GetString("cmu-corporate-alien-tech-cost"), "0", out _alienTechCostValueLabel,
            CrtTerminalPalette.Caution));

        _alienTechButton = new ConfirmButton
        {
            Text = Loc.GetString("cmu-corporate-alien-tech-request"),
            ConfirmationText = Loc.GetString("cmu-corporate-alien-tech-confirm"),
            MinWidth = 260,
        };
        CmuButtonStyles.Apply(_alienTechButton, CmuButtonStyles.Variant.Affirm);
        _alienTechButton.Label.FontOverride = _pmcKit.Bold;
        _alienTechButton.OnPressed += _ => OnRequestAlienTech?.Invoke();
        alienTech.AddChild(_alienTechButton);

        var statusStrip = CrtConsoleKit.Strip(CrtTerminalPalette.Surface1);
        _supportStatus = CrtConsoleKit.Wrapping(string.Empty, CrtTerminalPalette.TextDim);
        statusStrip.AddChild(_supportStatus);
        _pmcKit.Root.AddChild(statusStrip);

        _pmcKit.Root.AddChild(_pmcKit.Footer(out var footer));
        footer.Text = Loc.GetString("cmu-corporate-pmc-footer");
    }

    private void BuildDirectionTab()
    {
        _directionKit.Root.AddChild(_directionKit.Header(
            Loc.GetString("cmu-corporate-direction-heading"), out var subheading));
        subheading.Text = Loc.GetString("cmu-corporate-direction-subheading");

        _directionKit.Root.AddChild(_directionKit.Section(
            Loc.GetString("cmu-corporate-direction-current"), out var current));
        _directionCurrent = CrtConsoleKit.Text(
            Loc.GetString("cmu-company-direction-none"), _directionKit.Title, CrtTerminalPalette.TextBright);
        current.AddChild(_directionCurrent);
        current.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-direction-warning"), CrtTerminalPalette.Caution));

        var choices = CrtConsoleKit.Row(8);
        _roboticsDirection = new ConfirmButton
        {
            Text = Loc.GetString("cmu-company-direction-robotics"),
            ConfirmationText = Loc.GetString("cmu-corporate-direction-confirm"),
            HorizontalExpand = true,
        };
        CmuButtonStyles.Apply(_roboticsDirection, CmuButtonStyles.Variant.Affirm);
        _roboticsDirection.Label.FontOverride = _directionKit.Bold;
        _roboticsDirection.OnPressed += _ => OnSetDirection?.Invoke(CorporateResearchDirection.Robotics);
        choices.AddChild(_roboticsDirection);

        _bioengineeringDirection = new ConfirmButton
        {
            Text = Loc.GetString("cmu-company-direction-bioengineering"),
            ConfirmationText = Loc.GetString("cmu-corporate-direction-confirm"),
            HorizontalExpand = true,
        };
        CmuButtonStyles.Apply(_bioengineeringDirection, CmuButtonStyles.Variant.Affirm);
        _bioengineeringDirection.Label.FontOverride = _directionKit.Bold;
        _bioengineeringDirection.OnPressed += _ => OnSetDirection?.Invoke(CorporateResearchDirection.Bioengineering);
        choices.AddChild(_bioengineeringDirection);
        _directionKit.Root.AddChild(choices);

        _directionKit.Root.AddChild(_directionKit.Section(
            Loc.GetString("cmu-corporate-direction-future"), out var future));
        future.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-corporate-direction-future-hint"), CrtTerminalPalette.TextDim));

        _directionKit.Root.AddChild(_directionKit.Footer(out var footer));
        footer.Text = Loc.GetString("cmu-corporate-direction-footer");
    }

    private void ApplyTariff()
    {
        if (float.TryParse(_tariffInput.Text, out var percent))
            OnSetTariff?.Invoke(Math.Clamp(percent, 0, MaxTariff));
    }

    public void UpdateState(CorporateConsoleBuiState state)
    {
        // Operations.
        CrtConsoleKit.SetTag(_budgetTag, _budgetText, CrtConsoleKit.Money(state.CorporateBudget),
            state.CorporateBudget > 0 ? CrtTerminalPalette.Accent : CrtTerminalPalette.Alert);

        _tariff.Text = $"{MathF.Round(state.TransitTariffPercent):0}%";
        if (!_tariffInput.HasKeyboardFocus())
            _tariffInput.Text = $"{MathF.Round(state.TransitTariffPercent):0}";

        var incomeTax = state.EconomyStatus.IncomeTaxPercent;
        _taxNote.Text = incomeTax > 0
            ? Loc.GetString("cmu-corporate-withdraw-tax", ("percent", $"{MathF.Round(incomeTax):0}"))
            : Loc.GetString("cmu-corporate-withdraw-no-tax");

        _operationsKit.EconomyOverview(_economy, state.EconomyStatus);

        // Science.
        _chemistryPoints.Text = state.ChemistryPoints.ToString();
        _roboticsPoints.Text = state.RoboticsPoints.ToString();
        _companyValue.Text = state.CompanyValue.ToString();
        _scienceLevel.Text = state.ScienceLevel.ToString();
        _scienceDirection.Text = DirectionName(state.Direction);

        // Missions.
        _missionChemistry.Text = state.ChemistryPoints.ToString();
        _missionRobotics.Text = state.RoboticsPoints.ToString();
        _missionCompanyValue.Text = state.CompanyValue.ToString();
        var missionSignature = $"{state.MissionStatus}|" +
            string.Join(';', state.ActiveMissions.Select(MissionSignature)) + "|" +
            string.Join(';', state.AvailableMissions.Select(MissionSignature)) + "|" +
            string.Join(';', state.MissionHistory.Select(MissionSignature));
        if (missionSignature != _missionSignature)
        {
            _missionSignature = missionSignature;
            BuildMissions(state);
        }

        // Company support.
        _pmcCompanyValue.Text = state.CompanyValue.ToString();
        _pmcCostValue = state.PmcCostCompanyValue;
        _pmcCost.Text = state.PmcCostCompanyValue.ToString();
        _pmcCallsLeft = state.PmcCallsLeft;
        _pmcCooldownSeconds = state.PmcCooldownSeconds;
        _pmcCalls.Text = state.PmcCallsLeft.ToString();
        if (state.PmcCallsLeft <= 0)
            _pmcCooldown.Text = Loc.GetString("cmu-corporate-pmc-no-calls");
        else if (_pmcCooldownSeconds > 0)
            _pmcCooldown.Text = Loc.GetString("cmu-corporate-pmc-cooldown",
                ("seconds", _pmcCooldownSeconds));
        else
            _pmcCooldown.Text = Loc.GetString("cmu-corporate-pmc-ready");

        _pmcButton.Disabled = _pmcCallsLeft <= 0
                              || state.CompanyValue < _pmcCostValue
                              || _pmcCooldownSeconds > 0;

        _guardCostValue = state.GuardCostCompanyValue;
        _guardCost.Text = state.GuardCostCompanyValue.ToString();
        _guardCallsLeft = state.GuardCallsLeft;
        _guardCalls.Text = state.GuardCallsLeft.ToString();
        _guardCooldownSeconds = state.GuardCooldownSeconds;
        if (_guardCallsLeft <= 0)
            _guardCooldown.Text = Loc.GetString("cmu-corporate-guard-no-calls");
        else if (_guardCooldownSeconds > 0)
            _guardCooldown.Text = Loc.GetString("cmu-corporate-guard-cooldown",
                ("seconds", _guardCooldownSeconds));
        else
            _guardCooldown.Text = Loc.GetString("cmu-corporate-guard-ready");

        _guardButton.Disabled = _guardCallsLeft <= 0
                                || state.CompanyValue < _guardCostValue
                                || _guardCooldownSeconds > 0;

        _creditCostValue = state.CreditCostCompanyValue;
        _creditCost.Text = state.CreditCostCompanyValue.ToString();
        _creditAmount.Text = CrtConsoleKit.Money(state.CreditAmount);
        _creditButton.Disabled = state.CompanyValue < _creditCostValue;

        _alienTechCostValue = state.AlienTechCostCompanyValue;
        _alienTechCostValueLabel.Text = state.AlienTechCostCompanyValue.ToString();
        _alienTechCostValueLabel.FontColorOverride = state.CompanyValue < _alienTechCostValue
            ? CrtTerminalPalette.Alert
            : CrtTerminalPalette.Caution;
        _alienTechButton.Disabled = state.CompanyValue < _alienTechCostValue;

        SetRichText(_supportStatus, state.SupportStatus, CrtTerminalPalette.TextDim);

        // Direction.
        _directionCurrent.Text = DirectionName(state.Direction);
        _roboticsDirection.Disabled = state.Direction == CorporateResearchDirection.Robotics;
        _bioengineeringDirection.Disabled = state.Direction == CorporateResearchDirection.Bioengineering;
    }

    private void BuildMissions(CorporateConsoleBuiState state)
    {
        _missionBody.RemoveAllChildren();

        SetRichText(_missionStatus, state.MissionStatus, CrtTerminalPalette.TextDim);

        if (state.ActiveMissions.Count > 0)
        {
            _missionBody.AddChild(_missionKit.Section(
                Loc.GetString("cmu-corporate-missions-active"), out var active));
            foreach (var mission in state.ActiveMissions)
                active.AddChild(MissionCard(mission));
        }

        if (state.AvailableMissions.Count > 0)
        {
            _missionBody.AddChild(_missionKit.Section(
                Loc.GetString("cmu-corporate-missions-available"), out var available));
            foreach (var mission in state.AvailableMissions)
                available.AddChild(MissionCard(mission));
        }

        if (state.MissionHistory.Count > 0)
        {
            _missionBody.AddChild(_missionKit.Section(
                Loc.GetString("cmu-corporate-missions-history"), out var history));
            foreach (var mission in state.MissionHistory)
                history.AddChild(MissionCard(mission));
        }

        if (state.ActiveMissions.Count == 0
            && state.AvailableMissions.Count == 0
            && state.MissionHistory.Count == 0)
        {
            _missionBody.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-corporate-missions-empty"), CrtTerminalPalette.TextDim));
        }
    }

    private Control MissionCard(CompanyMissionInfo mission)
    {
        var accent = mission.Status switch
        {
            CompanyMissionStatus.Active => CrtTerminalPalette.Caution,
            CompanyMissionStatus.Completed => CrtTerminalPalette.Accent,
            CompanyMissionStatus.Failed => CrtTerminalPalette.Alert,
            _ => CrtTerminalPalette.Accent,
        };

        var card = _missionKit.Section(mission.Name, out var column, accent);
        column.AddChild(CrtConsoleKit.Wrapping(mission.Description, CrtTerminalPalette.TextDim));
        column.AddChild(_missionKit.Field(
            Loc.GetString("cmu-corporate-missions-objective"), mission.Objective));

        if (mission.Status == CompanyMissionStatus.Active)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-reward"),
                Loc.GetString("cmu-corporate-missions-reward-value", ("reward", mission.Reward)),
                CrtTerminalPalette.Accent));
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-progress"),
                Loc.GetString(
                    "cmu-corporate-missions-progress-value",
                    ("progress", mission.Progress),
                    ("target", mission.Target)),
                CrtTerminalPalette.Accent));
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-time"),
                Loc.GetString(
                    "cmu-corporate-missions-time-value",
                    ("seconds", mission.SecondsRemaining)),
                mission.SecondsRemaining < 120 ? CrtTerminalPalette.Alert : CrtTerminalPalette.TextBright));
        }

        if (mission.Status == CompanyMissionStatus.Available)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-reward"),
                Loc.GetString("cmu-corporate-missions-reward-value", ("reward", mission.Reward)),
                CrtTerminalPalette.Accent));
            var accept = _missionKit.Button(
                Loc.GetString("cmu-corporate-missions-accept"), CmuButtonStyles.Variant.Affirm);
            accept.OnPressed += _ => OnAcceptMission?.Invoke(mission.Id);
            column.AddChild(accept);
        }
        else if (mission.Status == CompanyMissionStatus.Completed)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-reward"),
                Loc.GetString("cmu-corporate-missions-reward-value", ("reward", mission.Reward)),
                CrtTerminalPalette.Accent));
        }
        else if (mission.Status == CompanyMissionStatus.Failed)
        {
            column.AddChild(_missionKit.Field(
                Loc.GetString("cmu-corporate-missions-status"),
                Loc.GetString("cmu-corporate-missions-failed"),
                CrtTerminalPalette.Alert));
        }

        return card;
    }

    private static string MissionSignature(CompanyMissionInfo mission)
    {
        return $"{mission.Id}:{mission.Status}:{mission.Progress}:{mission.Target}:{mission.Reward}:" +
               $"{mission.SecondsRemaining}";
    }

    private static string DirectionName(CorporateResearchDirection direction)
    {
        return direction switch
        {
            CorporateResearchDirection.Robotics => Loc.GetString("cmu-company-direction-robotics"),
            CorporateResearchDirection.Bioengineering => Loc.GetString("cmu-company-direction-bioengineering"),
            _ => Loc.GetString("cmu-company-direction-none"),
        };
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
