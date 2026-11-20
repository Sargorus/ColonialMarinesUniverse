using Content.Client.CMU14.Interface;
using Content.Client.UserInterface.Controls;
using Content.Shared.CMU14.Robotics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CMU14.Robotics;

public sealed partial class CMURoboticsOrderConsoleWindow
{
    private void BuildCartridges(CMURoboticsOrderConsoleState state)
    {
        _cartridgeBody.RemoveAllChildren();

        if (state.Cartridges.Count == 0)
        {
            _cartridgeBody.AddChild(CrtConsoleKit.Wrapping(
                Loc.GetString("cmu-robotics-cartridges-empty"),
                CrtTerminalPalette.TextDim));
            return;
        }

        foreach (var cartridge in state.Cartridges)
        {
            var id = cartridge.Prototype.Id;
            var name = GetPrototypeName(cartridge.Prototype);
            var section = _cartridgeKit.Section(name, out var column);

            if (!string.IsNullOrEmpty(cartridge.Description) &&
                Loc.TryGetString(cartridge.Description, out var description))
            {
                column.AddChild(CrtConsoleKit.Wrapping(
                    description,
                    CrtTerminalPalette.TextDim));
            }

            var affordable = state.RoboticsPoints >= cartridge.Cost;
            column.AddChild(_cartridgeKit.Field(
                Loc.GetString("cmu-robotics-cartridges-cost"),
                Loc.GetString(
                    "cmu-robotics-cartridges-cost-value",
                    ("cost", cartridge.Cost)),
                affordable ? CrtTerminalPalette.Accent : CrtTerminalPalette.Alert));

            var buy = _cartridgeKit.Button(
                Loc.GetString("cmu-robotics-cartridges-buy"),
                CmuButtonStyles.Variant.Affirm);
            buy.Disabled = !affordable;
            buy.OnPressed += _ => OnBuyCartridge?.Invoke(id);
            column.AddChild(buy);

            _cartridgeBody.AddChild(section);
        }
    }
}
