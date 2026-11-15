using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Content.Shared.CMU14.ColonyEconomy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleWindow
{
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

    private static string DirectionName(CorporateResearchDirection direction)
    {
        return direction switch
        {
            CorporateResearchDirection.Robotics => Loc.GetString("cmu-company-direction-robotics"),
            CorporateResearchDirection.Bioengineering => Loc.GetString("cmu-company-direction-bioengineering"),
            _ => Loc.GetString("cmu-company-direction-none"),
        };
    }
}
