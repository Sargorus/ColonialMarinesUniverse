using Content.Shared.CMU14.DroneOperator;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.DroneOperator;

// Assembly status shown when examining a partially built drone frame.
public sealed partial class CMUDroneOperatorSystem
{
    private void OnFrameExamined(Entity<CMUDroneFrameComponent> ent, ref ExaminedEvent args)
    {
        EnsureFramePartStates(ent);
        args.PushMarkup(Loc.GetString("cmu-drone-frame-examine-title"));

        foreach (var part in ent.Comp.RequiredParts)
        {
            var state = ent.Comp.PartStates.GetValueOrDefault(part, CMUDroneAssemblyPartState.Missing);
            args.PushMarkup(Loc.GetString(
                "cmu-drone-frame-examine-part",
                ("part", GetFramePartName(part)),
                ("state", Loc.GetString(StateLocKey(state)))));
        }

        if (!ent.Comp.PortsOpen)
        {
            args.PushMarkup(Loc.GetString("cmu-drone-frame-open-tool-required"));
            return;
        }

        if (TryGetFramePartWithState(ent, CMUDroneAssemblyPartState.Missing, out _))
        {
            args.PushMarkup(Loc.GetString("cmu-drone-frame-missing-parts"));
            return;
        }

        if (TryGetFramePartWithState(ent, CMUDroneAssemblyPartState.Installed, out var installed))
        {
            args.PushMarkup(Loc.GetString(
                "cmu-drone-frame-clamp-tool-required",
                ("part", GetFramePartName(installed))));
            return;
        }

        if (TryGetFramePartWithState(ent, CMUDroneAssemblyPartState.Clamped, out var clamped))
        {
            args.PushMarkup(Loc.GetString(
                "cmu-drone-frame-weld-tool-required",
                ("part", GetFramePartName(clamped))));
            return;
        }

        args.PushMarkup(Loc.GetString("cmu-drone-frame-ready-key"));
    }

    private static string StateLocKey(CMUDroneAssemblyPartState state)
    {
        return state switch
        {
            CMUDroneAssemblyPartState.Installed => "cmu-drone-frame-examine-installed",
            CMUDroneAssemblyPartState.Clamped => "cmu-drone-frame-examine-clamped",
            CMUDroneAssemblyPartState.Welded => "cmu-drone-frame-examine-welded",
            _ => "cmu-drone-frame-examine-missing",
        };
    }
}
