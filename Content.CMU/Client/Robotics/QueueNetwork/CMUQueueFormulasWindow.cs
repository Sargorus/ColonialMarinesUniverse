using System.Numerics;
using Content.Client.CMU14.Interface;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Always-available reference sheet for the M/M/1 formulas used by the
/// diagnostic terminal. Keeping it a separate window means the player can
/// leave it open while working on the network.
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

        var formulas = kit.Section(Loc.GetString("cmu-queue-formulas-mm1"), out var formulaColumn);
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-lambda"), Loc.GetString("cmu-queue-formulas-lambda-desc")));
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-mu"), Loc.GetString("cmu-queue-formulas-mu-desc")));
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-rho"), Loc.GetString("cmu-queue-formulas-rho-desc")));
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-stable"), Loc.GetString("cmu-queue-formulas-stable-desc")));
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-l"), Loc.GetString("cmu-queue-formulas-l-desc")));
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-lq"), Loc.GetString("cmu-queue-formulas-lq-desc")));
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-w"), Loc.GetString("cmu-queue-formulas-w-desc")));
        formulaColumn.AddChild(kit.Field(Loc.GetString("cmu-queue-formulas-wq"), Loc.GetString("cmu-queue-formulas-wq-desc")));
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
        networkColumn.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-network-equation"),
            CrtTerminalPalette.TextBright));
        networkColumn.AddChild(CrtConsoleKit.Wrapping(
            Loc.GetString("cmu-queue-formulas-network-desc"),
            CrtTerminalPalette.TextDim));
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
}
