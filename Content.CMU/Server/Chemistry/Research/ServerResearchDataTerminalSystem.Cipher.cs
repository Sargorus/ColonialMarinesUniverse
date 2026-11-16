using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Requisitions.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

// Cipher delivery and nearby XRF lookup.
public sealed partial class ServerResearchDataTerminalSystem
{
    private bool TryGetCipherElevator(EntityUid terminal, EntityUid actor, out Entity<RequisitionsElevatorComponent> elevator)
    {
        if (TryComp<MarineComponent>(actor, out var marine)
            && !string.IsNullOrEmpty(marine.Faction))
        {
            var actorQuery = EntityQueryEnumerator<RequisitionsElevatorComponent>();
            while (actorQuery.MoveNext(out var elevUid, out var elevComp))
            {
                if (elevComp.Faction.Equals(marine.Faction, StringComparison.OrdinalIgnoreCase))
                {
                    elevator = (elevUid, elevComp);
                    return true;
                }
            }
        }

        var xrf = GetNearestXRF(terminal);
        if (xrf != EntityUid.Invalid)
        {
            var net = _scanner.GetFactionElevator(xrf, null);
            if (net != NetEntity.Invalid
                && TryComp<RequisitionsElevatorComponent>(GetEntity(net), out var xrfComp))
            {
                elevator = (GetEntity(net), xrfComp);
                return true;
            }
        }

        elevator = default;
        return false;
    }

    public EntityUid GetNearestXRF(EntityUid ent)
    {
        if (!TryComp(ent, out TransformComponent? excomp))
            return EntityUid.Invalid;
        var scannersq = EntityQueryEnumerator<XRFScannerComponent>();
        List<(EntityUid, TransformComponent)> scanners = [];
        EntityUid closest = EntityUid.Invalid;
        float closestDistance = float.MaxValue;
        while (scannersq.MoveNext(out var xent, out var scanner))
        {
            if (!string.Equals(scanner.Faction, GetFaction(ent), StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TryComp(xent, out TransformComponent? xcomp))
                continue;
            if (excomp.MapUid != xcomp.MapUid)
                continue;
            float dist = System.Numerics.Vector2.Distance(_xform.GetWorldPosition(ent), _xform.GetWorldPosition(xent));
            if (closestDistance > dist)
            {
                closestDistance = dist;
                closest = xent;
            }
        }
        return closest;
    }

}
