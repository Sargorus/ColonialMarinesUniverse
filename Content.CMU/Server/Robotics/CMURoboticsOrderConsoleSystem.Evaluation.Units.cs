using System;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Medical.Anatomy.BodyParts;
using Content.Shared.CMU14.Robotics;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Robotics;

// Per-unit checks used by contract evaluation.
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    private int CountMissingLimbs(EntityUid unit, CMURoboticsOrder order)
    {
        if (!TryComp<BodyComponent>(unit, out var body))
            return ExternalLimbTypes.Length * 2;

        var missing = 0;
        foreach (var type in ExternalLimbTypes)
        {
            var valid = 0;
            foreach (var part in _body.GetBodyChildrenOfType(unit, type, body))
            {
                if (order.WorkingJoe
                    ? CMULimbCompatibility.IsWorkingJoeLimb(EntityManager, part.Id)
                    : CMULimbCompatibility.IsDroneLimb(EntityManager, part.Id))
                {
                    valid++;
                }
            }

            missing += Math.Max(0, 2 - valid);
        }

        return missing;
    }

    private bool HasRequiredModule(EntityUid unit, EntProtoId required)
    {
        return TryComp<CMUDroneAndroidComponent>(unit, out var android)
            && android.InstalledModule is { } module
            && MetaData(module).EntityPrototype?.ID == required.Id;
    }

    private int CountMissingOutfitElements(EntityUid unit, CMURoboticsOrderTargetComponent marker)
    {
        var missing = 0;
        foreach (var (slot, expected) in marker.Outfit)
        {
            if (!_inventory.TryGetSlotEntity(unit, slot, out var equipped)
                || MetaData(equipped.Value).EntityPrototype?.ID != expected.Id)
            {
                missing++;
            }
        }

        return missing;
    }
}
