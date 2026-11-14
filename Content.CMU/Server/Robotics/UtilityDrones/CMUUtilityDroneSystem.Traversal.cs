using System.Numerics;
using Content.Server.CMU14.ZLevels.Core;
using Content.Server.NPC.Components;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.CMU14.ZLevels.Core.Components;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private readonly List<CMUUtilityDroneStairRoute> _stairRoutes = new();
    private TimeSpan _nextStairScan;

    /// <summary>
    /// Handles multi-Z movement for a drone. Returns true when the route is
    /// still being handled (approaching, crossing or retrying) and false when
    /// normal local steering can take over on the target map.
    /// </summary>
    private bool TraverseToTarget(EntityUid drone, CMUUtilityDroneAgentComponent agent, EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return true;

        var map = Transform(drone).MapUid;
        var targetMap = Transform(target).MapUid;
        TryComp<CMUUtilityDroneTraversalComponent>(drone, out var traversal);
        if (map == targetMap && traversal?.Route == null)
            return false;

        if (map is not { } from || targetMap is not { } to || !_zLevels.IsSameZNetwork(from, to))
        {
            agent.PathAttempts++;
            _steering.Unregister(drone);
            return true;
        }

        traversal ??= EnsureComp<CMUUtilityDroneTraversalComponent>(drone);
        var position = _transform.GetWorldPosition(drone);
        if (traversal.Route is { } active)
        {
            if (TerminatingOrDeleted(active.Stair) || (from != active.FromMap && from != active.ToMap))
            {
                ClearTraversal(drone, traversal);
            }
            else if (from == active.ToMap &&
                     Vector2.Distance(position, active.Exit) < 0.75f)
            {
                ClearTraversal(drone, traversal);
                agent.PathAttempts = 0;
                return from != to;
            }
            else
            {
                // Progress must be measured toward the current waypoint. A
                // drone circling nearby used to refresh its deadline forever
                // because any movement counted as progress.
                var progressTarget = traversal.Crossing ? active.Exit : active.Approach;
                var progressDistance = Vector2.Distance(position, progressTarget);
                if (progressDistance + 0.05f < traversal.LastProgressDistance)
                {
                    traversal.LastProgressDistance = progressDistance;
                    traversal.ProgressDeadline = _timing.CurTime + TimeSpan.FromSeconds(8);
                }

                if (_timing.CurTime > traversal.ProgressDeadline ||
                    (!traversal.Crossing &&
                     TryComp<NPCSteeringComponent>(drone, out var failed) &&
                     failed.Status == SteeringStatus.NoPath))
                {
                    traversal.FailedStairs[active.Stair] = _timing.CurTime + TimeSpan.FromSeconds(15);
                    agent.PathAttempts++;
                    ClearTraversal(drone, traversal);
                    return true;
                }
            }
        }

        if (traversal.Route == null)
        {
            if (from == to)
                return false;

            RefreshStairRoutes();
            if (!TryComp<CMUZLevelMapComponent>(from, out var start) ||
                !TryComp<CMUZLevelMapComponent>(to, out var end))
            {
                agent.PathAttempts++;
                return true;
            }

            var best = float.MaxValue;
            foreach (var route in _stairRoutes)
            {
                if (route.FromMap != from ||
                    TerminatingOrDeleted(route.Stair) ||
                    traversal.FailedStairs.GetValueOrDefault(route.Stair) > _timing.CurTime ||
                    !TryComp<CMUZLevelMapComponent>(route.ToMap, out var next) ||
                    !_zLevels.IsSameZNetwork(route.ToMap, to) ||
                    Math.Abs((long) next.Depth - end.Depth) >=
                    Math.Abs((long) start.Depth - end.Depth))
                {
                    continue;
                }

                var score = Vector2.Distance(position, route.Approach) +
                            Vector2.Distance(route.Exit, _transform.GetWorldPosition(target));
                if (score >= best)
                    continue;

                traversal.Route = route;
                best = score;
            }

            traversal.LastProgressDistance = float.MaxValue;
            traversal.ProgressDeadline = _timing.CurTime + TimeSpan.FromSeconds(8);
        }

        if (traversal.Route is not { } selected)
        {
            agent.PathAttempts++;
            _steering.Unregister(drone);
            return true;
        }

        if (!traversal.Crossing &&
            (from == selected.ToMap ||
             Vector2.Distance(position, selected.Approach) < 0.3f ||
             OnStairLane(position, selected)))
        {
            traversal.Crossing = true;
            traversal.LastProgressDistance = float.MaxValue;
            _steering.Unregister(drone);
        }

        // Steering marks its old map destination NoPath after a physical map
        // transition. Re-register so the short crossing can continue.
        if (traversal.Crossing &&
            TryComp<NPCSteeringComponent>(drone, out var previous) &&
            previous.Status == SteeringStatus.NoPath)
        {
            _steering.Unregister(drone);
        }

        var destination = traversal.Crossing ? selected.Exit : selected.Approach;
        var xform = Transform(drone);
        var steering = _steering.Register(drone, _transform.ToCoordinates(
            xform.GridUid ?? from,
            new MapCoordinates(destination, xform.MapID)));
        ConfigureSteering(steering);
        steering.Range = 0.15f;
        if (traversal.Crossing)
        {
            steering.CurrentPath.Clear();
            steering.RepathRange = Vector2.Distance(position, destination) + 1;
        }

        return true;
    }

    private static bool OnStairLane(Vector2 position, CMUUtilityDroneStairRoute route)
    {
        var lane = route.Exit - route.Approach;
        if (lane.LengthSquared() < 0.0001f)
            return false;

        var along = Vector2.Dot(position - route.Approach, lane) / lane.LengthSquared();
        if (along <= 0f || along >= 1f)
            return false;

        return Vector2.Distance(position, route.Approach + lane * along) < 0.5f;
    }

    private void ClearTraversal(EntityUid drone, CMUUtilityDroneTraversalComponent traversal)
    {
        traversal.Route = null;
        traversal.Crossing = false;
        traversal.LastProgressDistance = float.MaxValue;
        _steering.Unregister(drone);
    }

    private void RefreshStairRoutes()
    {
        if (_nextStairScan > _timing.CurTime)
            return;

        _nextStairScan = _timing.CurTime + TimeSpan.FromSeconds(1);
        _stairRoutes.Clear();

        var ramps = EntityQueryEnumerator<CMUZLevelHighGroundComponent, TransformComponent>();
        while (ramps.MoveNext(out var uid, out var ramp, out var xform))
        {
            if (!xform.Anchored ||
                !ramp.Stick ||
                ramp.SupportOnlyFromAbove ||
                ramp.HeightCurve.Count < 2 ||
                xform.MapUid is not { } lower ||
                !_zLevels.TryMapUp(lower, out var upper))
            {
                continue;
            }

            var first = ramp.HeightCurve[0];
            var last = ramp.HeightCurve[^1];
            if (Math.Max(first, last) < 0.95f || Math.Abs(first - last) < 0.01f)
                continue;

            var direction = _transform.GetWorldRotation(uid).GetCardinalDir();
            var axis = ramp.Corner
                ? direction switch
                {
                    Direction.North => new Vector2(1, 1),
                    Direction.South => new Vector2(-1, -1),
                    Direction.East => new Vector2(1, -1),
                    _ => new Vector2(-1, 1),
                }
                : direction.ToVec();
            var uphill = first > last ? -axis : axis;
            var center = _transform.GetWorldPosition(uid);
            var bottom = center - uphill * (Math.Min(first, last) > 0.5f ? 2 : 1);
            var top = center + uphill;
            _stairRoutes.Add(new(uid, lower, upper.Value.Owner, bottom, top));
            _stairRoutes.Add(new(uid, upper.Value.Owner, lower, top, bottom));
        }

        var stairs = EntityQueryEnumerator<CMUZLevelStairsComponent, TransformComponent>();
        while (stairs.MoveNext(out var uid, out var stair, out var xform))
        {
            if (!xform.Anchored ||
                xform.MapUid is not { } from ||
                !_zLevels.TryMapOffset(from, stair.Offset, out var to))
            {
                continue;
            }

            var delta = stair.Direction.ToVec() * (stair.Offset >= 0 ? 1 : -1);
            var center = _transform.GetWorldPosition(uid);
            _stairRoutes.Add(new(uid, from, to.Value.Owner, center - delta, center + delta));
        }
    }
}
