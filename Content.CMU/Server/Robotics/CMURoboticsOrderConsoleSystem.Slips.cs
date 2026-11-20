using Content.Shared.CMU14.Robotics;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Robotics;

// Work-order slip formatting and printing.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private string BuildSlipContent(CMURoboticsOrder order, int minutes)
    {
        var type = Loc.GetString(order.Type == CMURoboticsOrderType.Build
            ? "cmu-robotics-console-type-build"
            : "cmu-robotics-console-type-repair");
        var module = order.RequiredModule is { } required
            ? GetPrototypeName(required)
            : Loc.GetString("cmu-robotics-slip-module-none");
        var package = Loc.GetString(order.Type == CMURoboticsOrderType.Build
            ? "cmu-robotics-slip-package-build"
            : "cmu-robotics-slip-package-repair");

        return Loc.GetString(
            "cmu-robotics-slip-content",
            ("id", order.Id),
            ("type", type),
            ("target", order.TargetName),
            ("module", module),
            ("amount", order.Amount),
            ("package", package),
            ("reward", order.Reward),
            ("minutes", minutes))
            // Fluent keeps the two characters "\n" in this single-line value;
            // the paper renderer expects real line breaks.
            .Replace("\\n", "\n");
    }

    private void OnPrintSlip(Entity<CMURoboticsOrderConsoleComponent> ent, ref CMURoboticsPrintSlipMessage args)
    {
        var pool = _pools.GetPool(ent.Owner);
        if (pool.ActiveOrder is not { } order)
            return;

        PrintSlip(ent, order, args.Actor);
        pool.Status = Loc.GetString("cmu-robotics-console-slip-printed");
        UpdateUi(ent);
    }

    private void PrintSlip(Entity<CMURoboticsOrderConsoleComponent> ent, CMURoboticsOrder order, EntityUid? actor)
    {
        var slip = Spawn(ent.Comp.SlipPrototype, Transform(ent.Owner).Coordinates);
        var slipComp = EnsureComp<CMURoboticsOrderSlipComponent>(slip);
        slipComp.OrderId = order.Id;
        slipComp.TargetName = order.TargetName;
        slipComp.Amount = order.Amount;
        slipComp.Reward = order.Reward;
        slipComp.Content = BuildSlipContent(order, (int) ent.Comp.OrderDuration.TotalMinutes);

        if (TryComp<PaperComponent>(slip, out var paper))
        {
            _meta.SetEntityName(slip, Loc.GetString("cmu-robotics-slip-name", ("id", order.Id)));
            _paper.SetContent((slip, paper), slipComp.Content);
        }

        if (actor is { } user && !TerminatingOrDeleted(user))
            _hands.TryPickupAnyHand(user, slip);
    }
}
