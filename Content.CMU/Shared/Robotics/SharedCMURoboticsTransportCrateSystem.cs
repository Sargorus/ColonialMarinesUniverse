using Content.Shared._RMC14.Synth;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Robotics;
using Content.Shared.DragDrop;
using Content.Shared.Lock;
using Robust.Shared.GameObjects;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Client-side drag target hint for the synthetic transport crate. The server
/// performs the real capacity, lock and size checks; the client only needs to
/// know that a synthetic or drone may be dropped here.
/// </summary>
public sealed partial class SharedCMURoboticsTransportCrateSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMURoboticsTransportCrateComponent, CanDropTargetEvent>(OnCanDropTarget);
    }

    private void OnCanDropTarget(Entity<CMURoboticsTransportCrateComponent> ent, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop = IsSyntheticOrDrone(args.Dragged) && !IsLocked(ent);
    }

    private bool IsSyntheticOrDrone(EntityUid entity)
    {
        return HasComp<SynthComponent>(entity) || HasComp<CMUDroneAndroidComponent>(entity);
    }

    private bool IsLocked(Entity<CMURoboticsTransportCrateComponent> ent)
    {
        return TryComp<LockComponent>(ent, out var lockComp) && lockComp.Locked;
    }
}
