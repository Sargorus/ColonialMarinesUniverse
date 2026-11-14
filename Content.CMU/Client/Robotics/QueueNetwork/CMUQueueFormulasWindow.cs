using System.Numerics;
using Content.Client.CMU14.Interface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Always-available reference sheet for the M/M/1 formulas used by the
/// diagnostic terminal. Each formula is shown as a large, centred card so it
/// reads as a formula instead of a line of body text.
/// </summary>
public sealed class CMUQueueFormulasWindow : DefaultWindow
{
    public CMUQueueFormulasWindow()
    {
        Title = Loc.GetString("cmu-queue-formulas-title");
        MinSize = new Vector2(500, 520);
        SetSize = new Vector2(600, 660);

        var kit = new CrtConsoleKit();
        kit.Root.AddChild(kit.Header(
            Loc.GetString("cmu-queue-formulas-heading"),
            out _));

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        var body = CrtConsoleKit.Column(8);
        body.HorizontalExpand = true;
        scroll.AddChild(body);
        kit.Root.AddChild(scroll);

        body.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-intro"),
            CrtTerminalPalette.TextDim));

        var formulas = kit.Section(Loc.GetString("cmu-queue-formulas-mm1"), out var formulaColumn);
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-lambda", "cmu-queue-formulas-lambda-desc", false);
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-mu", "cmu-queue-formulas-mu-desc", false);
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-rho", "cmu-queue-formulas-rho-desc");
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-stable", "cmu-queue-formulas-stable-desc");
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-l", "cmu-queue-formulas-l-desc");
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-lq", "cmu-queue-formulas-lq-desc");
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-w", "cmu-queue-formulas-w-desc");
        AddFormula(kit, formulaColumn, "cmu-queue-formulas-wq", "cmu-queue-formulas-wq-desc");
        body.AddChild(formulas);

        var negative = kit.Section(Loc.GetString("cmu-queue-formulas-negative"), out var negativeColumn);
        negativeColumn.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-negative-p1"),
            CrtTerminalPalette.Caution));
        negativeColumn.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-negative-p2"),
            CrtTerminalPalette.Text));
        negativeColumn.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-negative-p3"),
            CrtTerminalPalette.TextDim));
        body.AddChild(negative);

        var network = kit.Section(Loc.GetString("cmu-queue-formulas-network"), out var networkColumn);
        AddFormula(
            kit,
            networkColumn,
            "cmu-queue-formulas-network-equation",
            "cmu-queue-formulas-network-desc");
        body.AddChild(network);

        var example = kit.Section(Loc.GetString("cmu-queue-formulas-example"), out var exampleColumn);
        exampleColumn.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-example-body"),
            CrtTerminalPalette.Text));
        body.AddChild(example);

        var fix = kit.Section(Loc.GetString("cmu-queue-formulas-fix"), out var fixColumn);
        fixColumn.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-fix-body"),
            CrtTerminalPalette.TextDim));
        body.AddChild(fix);

        Contents.AddChild(kit.Tube);
    }

    private static void AddFormula(
        CrtConsoleKit kit,
        BoxContainer parent,
        string formulaKey,
        string descriptionKey,
        bool large = true)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = CrtTerminalPalette.Surface2,
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            },
        };

        var column = CrtConsoleKit.Column(2);
        var formula = CrtConsoleKit.Text(
            Loc.GetString(formulaKey),
            large ? kit.Huge : kit.Title,
            CrtTerminalPalette.Accent);
        formula.Align = Label.AlignMode.Center;
        formula.HorizontalExpand = true;
        column.AddChild(formula);
        column.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString(descriptionKey),
            CrtTerminalPalette.TextDim));
        panel.AddChild(column);
        parent.AddChild(panel);
    }
}
