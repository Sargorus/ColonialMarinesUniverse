using System.Numerics;
using Content.Shared._RMC14.Repairable;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Damage.Components;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private void ThinkRepair(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        if (!TryGetMarker(agent, step, out var marker))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-marker-missing");
            return;
        }

        if (agent.WorkTarget is not { } target ||
            TerminatingOrDeleted(target) ||
            !TryComp<RMCRepairableComponent>(target, out var repairable))
        {
            agent.WorkTarget = null;
            if (!TryFindRepairTarget(marker, step.Radius, out target, out repairable))
            {
                if (agent.NextRetry == TimeSpan.Zero)
                {
                    agent.NextRetry = _timing.CurTime + TimeSpan.FromSeconds(5);
                    agent.Activity = Loc.GetString("cmu-utility-drone-activity-repair-search");
                    _steering.Unregister(uid);
                    return;
                }

                if (_timing.CurTime >= agent.NextRetry)
                    FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-repair-none");

                return;
            }

            agent.WorkTarget = target;
            agent.WorkStarted = TimeSpan.Zero;
            agent.NextRetry = TimeSpan.Zero;
            agent.StepDeadline = _timing.CurTime + TimeSpan.FromSeconds(30);
        }

        if (!TryComp<DamageableComponent>(target, out var damageable))
        {
            agent.WorkTarget = null;
            CompleteStep(uid, agent);
            return;
        }

        var damage = _damageable.GetTotalDamage((target, damageable));
        if (damage <= 0)
        {
            agent.WorkTarget = null;
            CompleteStep(uid, agent);
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-repairing");
        var move = MoveToEntity(uid, agent, target, 2.0f);
        if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
            return;
        }

        if (move != MoveResult.Arrived)
            return;

        _steering.Unregister(uid);
        if (!TryGetRepairTool(uid, repairable, out var tool))
        {
            FailCurrentStep(uid, agent, true, "cmu-utility-drone-activity-repair-tool");
            return;
        }

        if (TryComp<ItemToggleComponent>(tool, out var toggle) &&
            !toggle.Activated &&
            !_itemToggle.TryActivate(tool, uid, predicted: false, showPopup: false))
        {
            FailCurrentStep(uid, agent, true, "cmu-utility-drone-activity-repair-tool");
            return;
        }

        if (agent.WorkStarted == TimeSpan.Zero)
        {
            if (!_interaction.InteractUsing(uid, tool, target, Transform(target).Coordinates))
            {
                FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-repair-failed");
                return;
            }

            agent.WorkStarted = _timing.CurTime;
            agent.NextRetry = _timing.CurTime + TimeSpan.FromSeconds(1);
            // The RMC repair delay scales with the construction skill. An
            // untrained utility drone can legitimately need tens of seconds.
            agent.StepDeadline = _timing.CurTime + TimeSpan.FromSeconds(120);
            return;
        }

        if (_timing.CurTime > agent.StepDeadline ||
            _timing.CurTime - agent.WorkStarted > TimeSpan.FromSeconds(120))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-repair-timeout");
        }
    }

    private bool TryGetRepairTool(EntityUid uid, RMCRepairableComponent repairable, out EntityUid tool)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!HasComp<BlowtorchComponent>(held) ||
                !_tool.HasQuality(held, repairable.Quality))
            {
                continue;
            }

            tool = held;
            return true;
        }

        tool = default;
        return false;
    }

    private bool TryFindRepairTarget(
        EntityUid marker,
        float radius,
        out EntityUid target,
        out RMCRepairableComponent repairable)
    {
        target = default;
        repairable = default!;
        _lookupBuffer.Clear();
        var searchRadius = Math.Clamp(radius, CMUUtilityDroneLimits.MinRadius, CMUUtilityDroneLimits.MaxRadius);
        _lookup.GetEntitiesInRange(Transform(marker).Coordinates, searchRadius, _lookupBuffer, LookupFlags.Uncontained);

        var candidates = 0;
        var bestDistance = float.MaxValue;
        var origin = _transform.GetWorldPosition(marker);
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;
            if (TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !TryComp<RMCRepairableComponent>(candidate, out var repairableCandidate) ||
                !TryComp<DamageableComponent>(candidate, out var damageable) ||
                _damageable.GetTotalDamage((candidate, damageable)) <= 0 ||
                !SameZNetwork(marker, candidate))
            {
                continue;
            }

            var distance = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(candidate));
            if (distance >= bestDistance)
                continue;

            target = candidate;
            repairable = repairableCandidate;
            bestDistance = distance;
        }

        return target.IsValid();
    }
}
