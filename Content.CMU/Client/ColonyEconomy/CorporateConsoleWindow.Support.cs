using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleWindow
{
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
}
