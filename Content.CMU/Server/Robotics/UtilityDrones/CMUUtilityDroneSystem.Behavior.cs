using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.NPC;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private enum MoveResult
    {
        Moving,
        Arrived,
        Failed,
    }

    private void ThinkMoveTo(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        if (!TryGetMarker(agent, step, out var marker))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-marker-missing");
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-moving");
        var move = MoveToEntity(uid, agent, marker, 0.9f);
        if (move == MoveResult.Arrived)
            CompleteStep(uid, agent);
        else if (move == MoveResult.Failed)
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
    }

    private void ThinkGuard(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        if (!TryGetMarker(agent, step, out var marker))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-marker-missing");
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-guarding");
        if (agent.WaitUntil != TimeSpan.Zero)
        {
            _steering.Unregister(uid);
            if (_timing.CurTime >= agent.WaitUntil)
                CompleteStep(uid, agent);
            return;
        }

        var angle = agent.Number * 2.39996323f;
        var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 0.8f;
        var move = MoveToMarkerOffset(uid, agent, marker, offset, 0.7f);
        if (move == MoveResult.Arrived)
        {
            agent.WaitUntil = _timing.CurTime + TimeSpan.FromSeconds(step.Seconds);
            _steering.Unregister(uid);
        }
        else if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
        }
    }

    private void ThinkFollow(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        var target = agent.FollowTarget;
        if (target is not { } followTarget ||
            TerminatingOrDeleted(followTarget) ||
            !IsAlive(followTarget) ||
            !SameZNetwork(uid, followTarget))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-follow-target");
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-following");
        var move = MoveToEntity(uid, agent, followTarget, 2.2f);
        if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
            return;
        }

        if (step.Seconds > 0f &&
            _timing.CurTime - agent.StepStarted >= TimeSpan.FromSeconds(step.Seconds))
        {
            CompleteStep(uid, agent);
        }
    }

    private void ThinkPatrol(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        if (!TryGetMarker(agent, step, out var marker))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-marker-missing");
            return;
        }

        if (agent.WaitUntil != TimeSpan.Zero)
        {
            _steering.Unregister(uid);
            if (_timing.CurTime < agent.WaitUntil)
                return;

            agent.WaitUntil = TimeSpan.Zero;
            agent.StepPhase++;
            agent.StepDeadline = _timing.CurTime + TimeSpan.FromSeconds(30);
            if (agent.StepPhase >= CMUUtilityDroneLimits.MaxWaypoints)
            {
                CompleteStep(uid, agent);
                return;
            }
        }

        if (agent.StepPhase < 0 || agent.StepPhase >= CMUUtilityDroneLimits.MaxWaypoints)
            agent.StepPhase = 0;

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-patrolling");
        var angle = (agent.StepPhase + agent.Number * 0.37f) * MathF.Tau / CMUUtilityDroneLimits.MaxWaypoints;
        var radius = Math.Clamp(step.Radius, CMUUtilityDroneLimits.MinRadius, CMUUtilityDroneLimits.MaxRadius);
        var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        var move = MoveToMarkerOffset(uid, agent, marker, offset, 0.8f);
        if (move == MoveResult.Arrived)
        {
            _steering.Unregister(uid);
            agent.WaitUntil = _timing.CurTime + TimeSpan.FromSeconds(0.75);
        }
        else if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
        }
    }

    private MoveResult MoveToEntity(EntityUid uid, CMUUtilityDroneAgentComponent agent, EntityUid target, float range)
    {
        if (TerminatingOrDeleted(target))
            return MoveResult.Failed;

        var targetMap = Transform(target).MapUid;
        var currentMap = Transform(uid).MapUid;
        var hasRoute = TryComp<CMUUtilityDroneTraversalComponent>(uid, out var traversal) &&
                       traversal.Route != null;
        if (targetMap != currentMap || hasRoute)
        {
            if (TraverseToTarget(uid, agent, target))
            {
                return agent.PathAttempts >= CMUUtilityDroneLimits.MaxPathAttempts
                    ? MoveResult.Failed
                    : MoveResult.Moving;
            }
        }

        return MoveToCoordinates(uid, agent, new EntityCoordinates(target, Vector2.Zero), range);
    }

    private MoveResult MoveToMarkerOffset(EntityUid uid, CMUUtilityDroneAgentComponent agent,
        EntityUid marker, Vector2 offset, float range)
    {
        if (TerminatingOrDeleted(marker))
            return MoveResult.Failed;

        var targetMap = Transform(marker).MapUid;
        var currentMap = Transform(uid).MapUid;
        var hasRoute = TryComp<CMUUtilityDroneTraversalComponent>(uid, out var traversal) &&
                       traversal.Route != null;
        if (targetMap != currentMap || hasRoute)
        {
            if (TraverseToTarget(uid, agent, marker))
            {
                return agent.PathAttempts >= CMUUtilityDroneLimits.MaxPathAttempts
                    ? MoveResult.Failed
                    : MoveResult.Moving;
            }
        }

        return MoveToCoordinates(uid, agent, new EntityCoordinates(marker, offset), range);
    }

    private MoveResult MoveToCoordinates(EntityUid uid, CMUUtilityDroneAgentComponent agent,
        EntityCoordinates destination, float range)
    {
        if (Reached(uid, destination, range))
        {
            _steering.Unregister(uid);
            return MoveResult.Arrived;
        }

        if (_timing.CurTime > agent.StepDeadline)
            return MoveResult.Failed;

        if (TryComp<NPCSteeringComponent>(uid, out var current))
        {
            if (current.Status == SteeringStatus.NoPath)
            {
                agent.PathAttempts++;
                _steering.Unregister(uid);
                return agent.PathAttempts >= CMUUtilityDroneLimits.MaxPathAttempts
                    ? MoveResult.Failed
                    : MoveResult.Moving;
            }

            if (current.Coordinates.Equals(destination))
                return MoveResult.Moving;
        }

        agent.Destination = destination;
        var steering = _steering.Register(uid, destination);
        ConfigureSteering(steering);
        steering.Range = range;
        return MoveResult.Moving;
    }

    private static void ConfigureSteering(NPCSteeringComponent steering)
    {
        // Utility drones inherit a synthetic body, not a combat HTN blackboard.
        steering.Flags = PathFlags.Climbing | PathFlags.Prying | PathFlags.Interact;
        steering.RepathRange = 0.2f;
    }

    private bool Reached(EntityUid uid, EntityCoordinates destination, float range)
    {
        return Transform(uid).Coordinates.TryDistance(EntityManager, _transform, destination, out var distance) &&
               distance <= range;
    }

    private bool TryGetMarker(CMUUtilityDroneAgentComponent agent, CMUUtilityStep step, out EntityUid marker)
    {
        marker = default;
        if (step.Marker is not { } markerUid ||
            TerminatingOrDeleted(markerUid) ||
            !HasComp<CMUUtilityTaskMarkerComponent>(markerUid) ||
            !Transform(markerUid).Anchored)
        {
            return false;
        }

        marker = markerUid;
        return true;
    }
}
