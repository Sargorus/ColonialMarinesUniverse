using Content.Shared._RMC14.Synth;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Robotics;
using Content.Shared.DragDrop;
using Content.Shared.Lock;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Lets players drag a synthetic or drone into a synthetic transport crate.
/// Entity storage only pulls nearby mobs in when it closes, so a dragged unit
/// needs an explicit insertion path. The crate still enforces its
/// one-synthetic limit (plus a control tablet), size limit, lock and
/// synthetic-only whitelist.
/// </summary>
public sealed partial class CMURoboticsTransportCrateSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMURoboticsTransportCrateComponent, DragDropTargetEvent>(OnDragDropTarget);
        SubscribeLocalEvent<CMURoboticsTransportCrateComponent, EntityStorageInsertedIntoAttemptEvent>(
            OnStorageInsertAttempt);
    }

    private void OnStorageInsertAttempt(
        Entity<CMURoboticsTransportCrateComponent> ent,
        ref EntityStorageInsertedIntoAttemptEvent args)
    {
        if (!IsSyntheticOrDrone(args.ItemToInsert)
            || !TryComp<EntityStorageComponent>(ent, out var storage))
            return;

        if (storage.Contents.Contains(args.ItemToInsert))
            return;

        foreach (var contained in storage.Contents.ContainedEntities)
        {
            if (IsSyntheticOrDrone(contained))
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    private void OnDragDropTarget(Entity<CMURoboticsTransportCrateComponent> ent, ref DragDropTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStoreSynthetic(ent, args.User, args.Dragged);
    }

    public bool CanStoreSynthetic(Entity<CMURoboticsTransportCrateComponent> ent, EntityUid dragged)
    {
        if (!TryComp<EntityStorageComponent>(ent, out var storage))
            return false;

        if (!IsSyntheticOrDrone(dragged))
            return false;

        if (TryComp<LockComponent>(ent, out var lockComp) && lockComp.Locked)
            return false;

        if (storage.Contents.Contains(dragged))
            return true;

        foreach (var contained in storage.Contents.ContainedEntities)
        {
            if (IsSyntheticOrDrone(contained))
                return false;
        }

        if (storage.Contents.ContainedEntities.Count >= storage.Capacity)
            return false;

        // CanInsert treats an open storage as always valid, so do the size and
        // attempt-event checks ourselves before closing the crate.
        if (storage.Open)
            return CanFitOpenStorage(dragged, storage);

        return _entityStorage.CanInsert(dragged, ent, storage);
    }

    public bool TryStoreSynthetic(
        Entity<CMURoboticsTransportCrateComponent> ent,
        EntityUid user,
        EntityUid dragged)
    {
        if (!TryComp<EntityStorageComponent>(ent, out var storage))
            return false;

        if (!CanStoreSynthetic(ent, dragged))
            return false;

        if (storage.Contents.Contains(dragged))
            return true;

        if (storage.Open)
            _entityStorage.CloseStorage((ent.Owner, storage), user);

        if (storage.Contents.Contains(dragged))
            return true;

        if (!_entityStorage.CanInsert(dragged, ent, storage))
            return false;

        return _entityStorage.Insert(dragged, ent, storage);
    }

    private bool CanFitOpenStorage(EntityUid dragged, EntityStorageComponent storage)
    {
        var aabb = _lookup.GetAABBNoContainer(dragged, default, 0);
        return storage.MaxSize >= aabb.Size.X && storage.MaxSize >= aabb.Size.Y;
    }

    private bool IsSyntheticOrDrone(EntityUid entity)
    {
        return HasComp<SynthComponent>(entity) || HasComp<CMUDroneAndroidComponent>(entity);
    }
}
