using System.Numerics;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private void ThinkReport(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        if (!TryGetMarker(agent, step, out var marker))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-marker-missing");
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-reporting");
        var move = MoveToEntity(uid, agent, marker, 1.5f);
        if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
            return;
        }

        if (move != MoveResult.Arrived)
            return;

        _steering.Unregister(uid);
        var radius = Math.Clamp(step.Radius, CMUUtilityDroneLimits.MinRadius, CMUUtilityDroneLimits.MaxRadius);
        if (!TryFindSubject(uid, Transform(marker).Coordinates, radius, out var subject))
        {
            if (agent.NextRetry == TimeSpan.Zero)
            {
                agent.NextRetry = _timing.CurTime + TimeSpan.FromSeconds(5);
                return;
            }

            if (_timing.CurTime >= agent.NextRetry)
            {
                FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-report-empty");
            }

            return;
        }

        if (TryReport(uid, agent, subject, step.Note))
        {
            agent.NextRetry = TimeSpan.Zero;
            CompleteStep(uid, agent);
        }
    }

    private bool TryFindSubject(
        EntityUid drone,
        EntityCoordinates origin,
        float radius,
        out EntityUid subject)
    {
        subject = default;
        _lookupBuffer.Clear();
        var searchRadius = Math.Clamp(radius, 1f, CMUUtilityDroneLimits.MaxRadius);
        _lookup.GetEntitiesInRange(origin, searchRadius, _lookupBuffer, LookupFlags.Uncontained);

        var candidates = 0;
        var bestDistance = float.MaxValue;
        var dronePosition = _transform.GetWorldPosition(drone);
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;
            if (TerminatingOrDeleted(candidate) ||
                candidate == drone ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !TryComp<MobStateComponent>(candidate, out var mob) ||
                !_mobState.IsAlive(candidate, mob) ||
                !_interaction.InRangeUnobstructed(drone, candidate, searchRadius))
            {
                continue;
            }

            var distance = Vector2.DistanceSquared(dronePosition, _transform.GetWorldPosition(candidate));
            if (distance >= bestDistance)
                continue;

            subject = candidate;
            bestDistance = distance;
        }

        return subject.IsValid();
    }
}
