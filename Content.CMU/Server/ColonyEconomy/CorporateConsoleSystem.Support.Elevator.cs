using System;
using Content.Shared._RMC14.Requisitions.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.ColonyEconomy;

// Corporate cargo elevator lookup for Company Support deliveries.
public sealed partial class CorporateConsoleSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    private Entity<RequisitionsElevatorComponent>? FindCorporateElevator(EntityUid console)
    {
        var map = _transform.GetMapId(console);
        var origin = _transform.GetWorldPosition(console);
        Entity<RequisitionsElevatorComponent>? closest = null;
        var closestDistance = float.MaxValue;

        var query = EntityQueryEnumerator<RequisitionsElevatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var elevator, out var xform))
        {
            if (xform.MapID != map
                || !string.Equals(elevator.Faction, "corporate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var distance = (origin - _transform.GetWorldPosition(uid)).LengthSquared();
            if (distance >= closestDistance)
                continue;

            closest = (uid, elevator);
            closestDistance = distance;
        }

        return closest;
    }
}
