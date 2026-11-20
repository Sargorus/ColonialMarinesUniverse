using System;
using System.Linq;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Database;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Robotics;

// Cartridge purchases for the robotics terminal.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private void OnPurchaseCartridge(
        Entity<CMURoboticsOrderConsoleComponent> ent,
        ref CMURoboticsPurchaseCartridgeMessage args)
    {
        var prototype = args.Prototype;
        var entry = ent.Comp.Cartridges.FirstOrDefault(candidate =>
            string.Equals(candidate.Cartridge.Id, prototype, StringComparison.Ordinal));
        if (entry == null)
        {
            ent.Comp.CartridgeStatus = Loc.GetString("cmu-robotics-cartridges-unavailable");
            UpdateUi(ent);
            return;
        }

        var pool = _pools.GetPool(ent.Owner);
        var faction = pool.Faction;
        var balance = _research.GetRoboticsPoints(faction);
        if (balance < entry.Cost)
        {
            ent.Comp.CartridgeStatus = Loc.GetString(
                "cmu-robotics-cartridges-no-funds",
                ("cost", entry.Cost),
                ("balance", balance));
            UpdateUi(ent);
            return;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(entry.Cartridge, out var cartridgePrototype)
            || !cartridgePrototype.TryComp<CMURoboticsRecipeCartridgeComponent>(out _, _compFactory))
        {
            ent.Comp.CartridgeStatus = Loc.GetString("cmu-robotics-cartridges-unavailable");
            UpdateUi(ent);
            return;
        }

        var cartridge = Spawn(entry.Cartridge, Transform(ent.Owner).Coordinates);
        if (args.Actor.IsValid() && !TerminatingOrDeleted(args.Actor))
            _hands.TryPickupAnyHand(args.Actor, cartridge);

        _research.UpdateRoboticsPoints(balance - entry.Cost, faction);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor)} bought the robotics cartridge {entry.Cartridge.Id} " +
            $"for {entry.Cost} robotics points ({faction}) at {ToPrettyString(ent.Owner)}");
        ent.Comp.CartridgeStatus = Loc.GetString(
            "cmu-robotics-cartridges-purchased",
            ("cartridge", GetPrototypeName(entry.Cartridge)));
        UpdateUi(ent);
    }
}
