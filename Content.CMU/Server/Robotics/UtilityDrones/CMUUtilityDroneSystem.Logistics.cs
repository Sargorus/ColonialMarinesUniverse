using System.Numerics;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private void ThinkFetch(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        EntityUid? searchMarker = null;
        if (step.Target is { } selectedTarget)
        {
            if (!IsValidFetchTarget(selectedTarget))
            {
                FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-fetch-target");
                return;
            }

            if (IsHolding(uid, selectedTarget))
            {
                agent.FetchedItem = selectedTarget;
                CompleteStep(uid, agent);
                return;
            }

            agent.WorkTarget = selectedTarget;
        }
        else
        {
            if (!TryGetMarker(agent, step, out var marker))
            {
                FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-marker-missing");
                return;
            }

            searchMarker = marker;
        }

        if (agent.FetchedItem is { } held)
        {
            if (TerminatingOrDeleted(held))
            {
                agent.FetchedItem = null;
            }
            else if (IsHolding(uid, held) &&
                     (step.Target == null || step.Target == held))
            {
                CompleteStep(uid, agent);
                return;
            }
            else
            {
                agent.FetchedItem = null;
            }
        }

        if (agent.WorkTarget is not { } item || !IsValidFetchTarget(item))
        {
            if (searchMarker is not { } marker)
            {
                FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-fetch-target");
                return;
            }

            if (!TryFindFetchItem(uid, marker, step.Radius, out item))
            {
                if (agent.NextRetry == TimeSpan.Zero)
                {
                    agent.NextRetry = _timing.CurTime + TimeSpan.FromSeconds(5);
                    agent.Activity = Loc.GetString("cmu-utility-drone-activity-fetch-search");
                    _steering.Unregister(uid);
                    return;
                }

                if (_timing.CurTime >= agent.NextRetry)
                {
                    FailCurrentStep(uid, agent, true, "cmu-utility-drone-activity-fetch-empty");
                }

                return;
            }

            agent.WorkTarget = item;
            agent.StepDeadline = _timing.CurTime + TimeSpan.FromSeconds(30);
            agent.NextRetry = TimeSpan.Zero;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-fetching");
        var move = MoveToEntity(uid, agent, item, 1.2f);
        if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
            return;
        }

        if (move != MoveResult.Arrived)
            return;

        _steering.Unregister(uid);
        if (!_hands.TryPickupAnyHand(uid, item))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-fetch-grip");
            return;
        }

        agent.FetchedItem = item;
        agent.WorkTarget = null;
        CompleteStep(uid, agent);
    }

    private bool IsValidFetchTarget(EntityUid target)
    {
        return !TerminatingOrDeleted(target) &&
               !Transform(target).Anchored &&
               HasComp<ItemComponent>(target);
    }

    private void ThinkPull(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        if (step.Target is not { } target ||
            TerminatingOrDeleted(target) ||
            Transform(target).Anchored)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-pull-target");
            return;
        }

        if (_pulling.GetPulling(uid) == target)
        {
            CompleteStep(uid, agent);
            return;
        }

        if (!TryStopCurrentPull(uid))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-pull-stop");
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-pulling");
        var move = MoveToEntity(uid, agent, target, 1.2f);
        if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
            return;
        }

        if (move != MoveResult.Arrived)
            return;

        _steering.Unregister(uid);
        if (!_pulling.TryStartPull(uid, target))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-pull-failed");
            return;
        }

        CheckPullTriggers(uid, agent);
        if (ProcessPendingTriggers(uid, agent))
            return;

        CompleteStep(uid, agent);
    }

    private void ThinkStopPull(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        if (!TryStopCurrentPull(uid))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-pull-stop");
            return;
        }

        CheckPullTriggers(uid, agent);
        if (ProcessPendingTriggers(uid, agent))
            return;

        CompleteStep(uid, agent);
    }

    private bool TryStopCurrentPull(EntityUid uid)
    {
        if (_pulling.GetPulling(uid) is not { } pulled)
            return true;

        if (!TryComp<PullableComponent>(pulled, out var pullable))
            return false;

        return _pulling.TryStopPull(pulled, pullable, uid);
    }

    private void ThinkDeliver(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        if (!TryGetMarker(agent, step, out var marker))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-marker-missing");
            return;
        }

        if (!TryGetCarriedItem(uid, agent, out var item))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-deliver-empty");
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-delivering");
        var move = MoveToEntity(uid, agent, marker, 1.6f);
        if (move == MoveResult.Failed)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-path-failed");
            return;
        }

        if (move != MoveResult.Arrived)
            return;

        _steering.Unregister(uid);
        if (TryInsertIntoNearbyStorage(uid, marker, item))
        {
            agent.FetchedItem = null;
            CompleteStep(uid, agent);
            return;
        }

        // The marker itself is a valid delivery point. Dropping the item there
        // is physical and still satisfies the delivery contract.
        if (_hands.TryDrop(uid, item, new EntityCoordinates(marker, new System.Numerics.Vector2(0.1f, -0.1f))))
        {
            agent.FetchedItem = null;
            CompleteStep(uid, agent);
            return;
        }

        FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-deliver-failed");
    }

    private bool TryGetCarriedItem(EntityUid uid, CMUUtilityDroneAgentComponent agent, out EntityUid item)
    {
        if (agent.FetchedItem is { } fetched &&
            !TerminatingOrDeleted(fetched) &&
            IsHolding(uid, fetched))
        {
            item = fetched;
            return true;
        }

        if (_hands.TryGetActiveItem(uid, out var active))
        {
            item = active.Value;
            return true;
        }

        item = default;
        return false;
    }

    private bool IsHolding(EntityUid uid, EntityUid item)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (held == item)
                return true;
        }

        return false;
    }

    private bool TryFindFetchItem(
        EntityUid drone,
        EntityUid marker,
        float radius,
        out EntityUid item)
    {
        item = default;
        _lookupBuffer.Clear();
        var searchRadius = Math.Clamp(radius, 1f, 8f);
        _lookup.GetEntitiesInRange(Transform(marker).Coordinates, searchRadius, _lookupBuffer, LookupFlags.Uncontained);

        var candidates = 0;
        var bestDistance = float.MaxValue;
        var origin = _transform.GetWorldPosition(marker);
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;
            if (TerminatingOrDeleted(candidate) ||
                candidate == drone ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !HasComp<ItemComponent>(candidate) ||
                Transform(candidate).Anchored)
            {
                continue;
            }

            var distance = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(candidate));
            if (distance >= bestDistance)
                continue;

            item = candidate;
            bestDistance = distance;
        }

        return item.IsValid();
    }

    private bool TryInsertIntoNearbyStorage(EntityUid drone, EntityUid marker, EntityUid item)
    {
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(Transform(marker).Coordinates, 1.8f, _lookupBuffer, LookupFlags.Uncontained);
        var candidates = 0;
        foreach (var container in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;
            if (TerminatingOrDeleted(container) ||
                container == drone ||
                container == item ||
                container == marker ||
                !SameZNetwork(marker, container))
            {
                continue;
            }

            var hasEntityStorage = HasComp<EntityStorageComponent>(container);
            var hasStorage = HasComp<StorageComponent>(container);
            if (!hasEntityStorage && !hasStorage)
                continue;

            if (IsHolding(drone, item))
                _hands.TryDrop(drone, item);

            if (hasEntityStorage && _entityStorage.Insert(item, container))
                return true;

            if (hasStorage && _storage.Insert(container, item, out _, out _, drone))
                return true;

            if (!TerminatingOrDeleted(item) && !IsHolding(drone, item))
                _hands.TryPickupAnyHand(drone, item);
        }

        return false;
    }
}
