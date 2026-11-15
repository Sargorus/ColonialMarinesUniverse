using System;
using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleWindow
{
    /// <summary>The highest tariff the console accepts.</summary>
    private const int MaxTariff = 50;

    private static readonly int[] TariffPresets = [0, 10, 20, 30, 40, 50];

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

    private void ApplyTariff()
    {
        if (float.TryParse(_tariffInput.Text, out var percent))
            OnSetTariff?.Invoke(Math.Clamp(percent, 0, MaxTariff));
    }
}
