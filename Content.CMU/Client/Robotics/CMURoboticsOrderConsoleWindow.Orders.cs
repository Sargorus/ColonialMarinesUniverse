using System;
using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Content.Shared.CMU14.Robotics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.Robotics;

public sealed partial class CMURoboticsOrderConsoleWindow
{
    private void BuildActive(CMURoboticsOrderInfo? active)
    {
        var section = _kit.Section(Loc.GetString("cmu-robotics-console-active"), out var column);

        if (active is not { } order)
        {
            column.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-robotics-console-no-active"),
                CrtTerminalPalette.TextDim));
            _body.AddChild(section);
            return;
        }

        AddOrderFields(column, order);

        var print = _kit.Button(
            Loc.GetString("cmu-robotics-console-print-slip"),
            CmuButtonStyles.Variant.Neutral);
        print.OnPressed += _ => OnPrintSlip?.Invoke();
        column.AddChild(print);

        var cancel = _kit.Button(
            Loc.GetString("cmu-robotics-console-cancel"),
            CmuButtonStyles.Variant.Caution);
        cancel.OnPressed += _ => OnCancel?.Invoke();
        column.AddChild(cancel);

        var hint = order.Type == CMURoboticsOrderType.Repair
            ? Loc.GetString(
                "cmu-robotics-console-delivery-hint-repair",
                ("amount", order.Amount))
            : Loc.GetString("cmu-robotics-console-delivery-hint-build");
        column.AddChild(CrtConsoleKit.Wrapping(hint, CrtTerminalPalette.TextDim));
        _body.AddChild(section);
    }

    private void BuildAvailable(CMURoboticsOrderConsoleState state)
    {
        var section = _kit.Section(Loc.GetString("cmu-robotics-console-available"), out var column);

        if (state.Available.Count == 0)
        {
            column.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-robotics-console-no-orders"),
                CrtTerminalPalette.TextDim));
        }
        else
        {
            foreach (var order in state.Available)
            {
                var card = _kit.Section(
                    Loc.GetString(
                        "cmu-robotics-console-order-heading",
                        ("type", TypeName(order.Type)),
                        ("target", Loc.GetString(order.TargetNameLocId))),
                    out var details);

                AddOrderFields(details, order);

                var accept = _kit.Button(
                    Loc.GetString("cmu-robotics-console-accept"),
                    CmuButtonStyles.Variant.Affirm);
                accept.Disabled = state.Active != null || state.CooldownSeconds > 0;
                var id = order.Id;
                accept.OnPressed += _ => OnAccept?.Invoke(id);
                details.AddChild(accept);

                column.AddChild(card);
            }
        }

        var refresh = _kit.Button(
            Loc.GetString("cmu-robotics-console-refresh"),
            CmuButtonStyles.Variant.Neutral);
        refresh.Disabled = state.Active != null;
        refresh.OnPressed += _ => OnRefresh?.Invoke();
        column.AddChild(refresh);

        _body.AddChild(section);
    }

    private void AddOrderFields(BoxContainer column, CMURoboticsOrderInfo order)
    {
        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-type"),
            TypeName(order.Type)));

        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-target"),
            Loc.GetString(order.TargetNameLocId)));

        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-amount"),
            order.Amount.ToString()));

        if (order.RequiredModule is { } module)
        {
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-robotics-console-module"),
                GetPrototypeName(module)));
        }

        if (order.SecondsRemaining > 0)
        {
            column.AddChild(_kit.Field(
                Loc.GetString("cmu-robotics-console-time-left"),
                TimeSpan.FromSeconds(order.SecondsRemaining).ToString(@"mm\:ss")));
        }

        column.AddChild(_kit.Field(
            Loc.GetString("cmu-robotics-console-reward"),
            CrtConsoleKit.Money(order.Reward),
            CrtTerminalPalette.Accent));
    }
}
