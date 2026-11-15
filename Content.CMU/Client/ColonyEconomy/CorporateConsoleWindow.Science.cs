using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleWindow
{
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
}
