using System.Globalization;
using System.Linq;
using Content.Shared.Chat;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Damage.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.NPC;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._RMC14.Targeting;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private bool TickScriptAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        ICMUUtilityScriptAction runningAction)
    {
        if (runningAction is not CMUUtilityScriptActionState action)
            return false;

        var now = _timing.CurTime;
        switch (action.Kind)
        {
            case CMUUtilityScriptActionKind.Say:
                if (now < agent.NextChat)
                    return true;

                if (!string.IsNullOrWhiteSpace(action.Text))
                {
                    _chat.TrySendInGameICMessage(
                        uid,
                        action.Text,
                        InGameICChatType.Speak,
                        false,
                        ignoreActionBlocker: true);
                    agent.NextChat = now + TimeSpan.FromSeconds(1);
                }

                frame.Action = null;
                return false;

            case CMUUtilityScriptActionKind.Wait:
                if (now < action.WaitUntil)
                    return true;

                frame.Action = null;
                return false;

            case CMUUtilityScriptActionKind.Move:
                if (!TryResolveActionTarget(frame, out var moveTarget))
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-target");
                    return true;
                }

                return TickMoveAction(uid, agent, frame, moveTarget, 0.9f);

            case CMUUtilityScriptActionKind.Follow:
                var followTarget = agent.FollowTarget;
                if (followTarget is not { } follow ||
                    TerminatingOrDeleted(follow) ||
                    !IsAlive(follow) ||
                    !SameZNetwork(uid, follow))
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-follow");
                    return true;
                }

                if (action.Seconds > 0 &&
                    now - action.Started >= TimeSpan.FromSeconds(action.Seconds))
                {
                    frame.Action = null;
                    return false;
                }

                var followResult = MoveToEntity(uid, agent, follow, 2.2f);
                if (followResult == MoveResult.Failed)
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
                    return true;
                }

                return true;

            case CMUUtilityScriptActionKind.Patrol:
                return TickPatrolAction(uid, agent, frame);

            case CMUUtilityScriptActionKind.Take:
                if (!TryResolveActionTarget(frame, out var takeTarget) ||
                    !IsValidFetchTarget(takeTarget))
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-item");
                    return true;
                }

                agent.Activity = Loc.GetString("cmu-utility-drone-activity-fetching");
                var takeResult = MoveToEntity(uid, agent, takeTarget, 1.2f);
                if (takeResult == MoveResult.Failed)
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
                    return true;
                }

                if (takeResult != MoveResult.Arrived)
                    return true;

                if (!TryPickupScriptItem(uid, takeTarget, action.Hand))
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-hand");
                    return true;
                }

                frame.Action = null;
                return false;

            case CMUUtilityScriptActionKind.MoveCoordinates:
                if (action.Coordinates is not { } coordinates)
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-target");
                    return true;
                }

                return TickMoveCoordinatesAction(uid, agent, frame, coordinates);

            case CMUUtilityScriptActionKind.TakeBackpack:
                return TickTakeBackpackAction(uid, frame);

            case CMUUtilityScriptActionKind.PutBackpack:
                return TickPutBackpackAction(uid, frame);

            case CMUUtilityScriptActionKind.Use:
                return TickUseAction(uid, agent, frame);

            case CMUUtilityScriptActionKind.Shoot:
                return TickShootScriptAction(uid, agent, frame);

            case CMUUtilityScriptActionKind.Fight:
                return TickFightScriptAction(uid, agent, frame);

            case CMUUtilityScriptActionKind.Repair:
                return TickRepairScriptAction(uid, agent, frame);

            case CMUUtilityScriptActionKind.Pull:
                return TickPullAction(uid, agent, frame);

            case CMUUtilityScriptActionKind.Release:
                if (!TryStopCurrentPull(uid))
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-pull");
                    return true;
                }

                CheckPullTriggers(uid, agent);
                frame.Action = null;
                ProcessPendingTriggers(uid, agent);
                return false;

            case CMUUtilityScriptActionKind.Return:
                if (agent.HomeAnchor is not { } home ||
                    TerminatingOrDeleted(home) ||
                    !SameZNetwork(uid, home))
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-home");
                    return true;
                }

                agent.Activity = Loc.GetString("cmu-utility-drone-activity-returning");
                var returnResult = MoveToEntity(uid, agent, home, 1.8f);
                if (returnResult == MoveResult.Failed)
                {
                    FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
                    return true;
                }

                if (returnResult == MoveResult.Arrived)
                {
                    frame.Action = null;
                    return false;
                }

                return true;

            default:
                frame.Action = null;
                return false;
        }
    }

    private bool TickMoveAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid target,
        float range)
    {
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-script-moving");
        var result = MoveToEntity(uid, agent, target, range);
        if (result == MoveResult.Failed)
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
            return true;
        }

        if (result == MoveResult.Arrived)
        {
            frame.Action = null;
            return false;
        }

        return true;
    }

    private bool TickMoveCoordinatesAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityCoordinates coordinates)
    {
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-script-moving");
        var result = MoveToCoordinates(uid, agent, coordinates, 0.8f);
        if (result == MoveResult.Failed)
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
            return true;
        }

        if (result == MoveResult.Arrived)
        {
            frame.Action = null;
            return false;
        }

        return true;
    }

    private bool TickUseAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame)
    {
        if (!TryResolveActionTarget(frame, out var target))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-target");
            return true;
        }

        if (frame.Action?.Item is not { } item ||
            TerminatingOrDeleted(item) ||
            !_hands.EnumerateHeld(uid).Contains(item))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-item");
            return true;
        }

        if (target == uid || target == item)
        {
            _interaction.UseInHandInteraction(uid, item);
            frame.Action = null;
            return false;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-using");
        var result = MoveToEntity(uid, agent, target, 1.2f);
        if (result == MoveResult.Failed)
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
            return true;
        }

        if (result != MoveResult.Arrived)
            return true;

        _interaction.InteractUsing(uid, item, target, Transform(target).Coordinates);
        frame.Action = null;
        return false;
    }

    private bool TickPatrolAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame)
    {
        if (frame.Action is not { } action ||
            !TryResolveActionTarget(frame, out var marker))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-target");
            return true;
        }

        if (action.WaitUntil != TimeSpan.Zero)
        {
            if (_timing.CurTime < action.WaitUntil)
                return true;

            action.WaitUntil = TimeSpan.Zero;
            action.Phase++;
        }

        if (action.Phase >= CMUUtilityDroneLimits.MaxWaypoints)
        {
            frame.Action = null;
            return false;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-patrolling");
        var angle = (action.Phase + agent.Number * 0.37f) *
                    MathF.Tau /
                    CMUUtilityDroneLimits.MaxWaypoints;
        var radius = Math.Clamp(action.Radius,
            CMUUtilityDroneLimits.MinRadius,
            CMUUtilityDroneLimits.MaxRadius);
        var offset = new System.Numerics.Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        var result = MoveToMarkerOffset(uid, agent, marker, offset, 0.8f);
        if (result == MoveResult.Failed)
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
            return true;
        }

        if (result == MoveResult.Arrived)
        {
            action.WaitUntil = _timing.CurTime +
                               TimeSpan.FromSeconds(Math.Max(0.1f, action.Seconds));
        }

        return true;
    }

    private bool TickPullAction(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityScriptFrame frame)
    {
        if (!TryResolveActionTarget(frame, out var target) ||
            TerminatingOrDeleted(target) ||
            Transform(target).Anchored)
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-item");
            return true;
        }

        if (_pulling.GetPulling(uid) == target)
        {
            frame.Action = null;
            return false;
        }

        if (!TryStopCurrentPull(uid))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-pull");
            return true;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-pulling");
        var result = MoveToEntity(uid, agent, target, 1.2f);
        if (result == MoveResult.Failed)
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-path");
            return true;
        }

        if (result != MoveResult.Arrived)
            return true;

        if (!_pulling.TryStartPull(uid, target))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-pull");
            return true;
        }

        CheckPullTriggers(uid, agent);
        if (ProcessPendingTriggers(uid, agent))
        {
            frame.Action = null;
            return false;
        }

        frame.Action = null;
        return false;
    }

    private bool TickTakeBackpackAction(EntityUid uid, CMUUtilityScriptFrame frame)
    {
        if (!TryGetBackpack(uid, out var backpack))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-backpack");
            return true;
        }

        if (!TryResolveBackpackItem(backpack, frame, out var item))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-item");
            return true;
        }

        if (!RemoveFromBackpack(backpack, item))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-backpack");
            return true;
        }

        if (!TryPickupScriptItem(uid, item, frame.Action?.Hand ?? string.Empty))
        {
            InsertBackpackItem(backpack, item, uid);
            FailScriptAction(frame, "cmu-utility-drone-script-fail-hand");
            return true;
        }

        frame.Action = null;
        return false;
    }

    private bool TryPickupScriptItem(EntityUid uid, EntityUid item, string hand)
    {
        if (string.IsNullOrWhiteSpace(hand))
            return _hands.TryPickupAnyHand(uid, item);

        return TryResolveHandId(uid, hand, out var handId) &&
               _hands.TryPickup(uid, item, handId);
    }

    private bool TickPutBackpackAction(EntityUid uid, CMUUtilityScriptFrame frame)
    {
        if (!TryGetBackpack(uid, out var backpack))
        {
            FailScriptAction(frame, "cmu-utility-drone-script-fail-backpack");
            return true;
        }

        var item = frame.Action?.Target;
        if (item is not { } itemUid ||
            TerminatingOrDeleted(itemUid) ||
            !_hands.EnumerateHeld(uid).Contains(itemUid))
        {
            if (!_hands.TryGetActiveItem(uid, out var active))
            {
                FailScriptAction(frame, "cmu-utility-drone-script-fail-item");
                return true;
            }

            itemUid = active.Value;
        }

        if (_hands.TryDrop(uid, itemUid))
        {
            if (InsertBackpackItem(backpack, itemUid, uid))
            {
                frame.Action = null;
                return false;
            }

            _hands.TryPickupAnyHand(uid, itemUid);
        }

        FailScriptAction(frame, "cmu-utility-drone-script-fail-backpack");
        return true;
    }

    private void StartMoveAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid target)
    {
        if (TerminatingOrDeleted(target))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
            return;
        }

        PrepareScriptAction(uid, agent, frame, CMUUtilityScriptActionKind.Move, target);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-script-moving");
    }

    private void StartMoveCoordinatesAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityCoordinates coordinates)
    {
        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.MoveCoordinates);
        action.Coordinates = coordinates;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-script-moving");
    }

    private void StartUseAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid item,
        EntityUid target)
    {
        if (TerminatingOrDeleted(item) ||
            !_hands.EnumerateHeld(uid).Contains(item))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-item");
            return;
        }

        if (TerminatingOrDeleted(target))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
            return;
        }

        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.Use,
            target);
        action.Item = item;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-using");
    }

    private void StartFollowAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        double seconds)
    {
        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.Follow);
        action.Seconds = (float) Math.Min(seconds, 600);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-following");
    }

    private void StartPatrolAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid marker,
        float radius,
        float seconds)
    {
        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.Patrol,
            marker);
        action.Radius = Math.Clamp(
            radius,
            CMUUtilityDroneLimits.MinRadius,
            CMUUtilityDroneLimits.MaxRadius);
        action.Seconds = Math.Clamp(seconds, 0f, 600f);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-patrolling");
    }

    private void StartReturnAction(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityScriptFrame frame)
    {
        if (agent.HomeAnchor is not { } home || TerminatingOrDeleted(home))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-home");
            return;
        }

        PrepareScriptAction(uid, agent, frame, CMUUtilityScriptActionKind.Return, home);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-returning");
    }

    private void StartTakeAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid target,
        string hand = "")
    {
        if (!IsValidFetchTarget(target))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-item");
            return;
        }

        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.Take,
            target);
        action.Hand = hand;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-fetching");
    }

    private void StartTakeBackpackAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        CMUUtilityScriptValue value,
        string hand = "")
    {
        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.TakeBackpack,
            value.Type == CMUUtilityScriptValueType.Entity ? value.Entity : null);
        action.Text = value.Type == CMUUtilityScriptValueType.String
            ? value.Text
            : string.Empty;
        action.Hand = hand;
    }

    private void StartPutBackpackAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        CMUUtilityScriptValue value)
    {
        PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.PutBackpack,
            value.Type == CMUUtilityScriptValueType.Entity ? value.Entity : null);
    }

    private void StartPullAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid target)
    {
        PrepareScriptAction(uid, agent, frame, CMUUtilityScriptActionKind.Pull, target);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-pulling");
    }

    private void StartReleaseAction(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityScriptFrame frame)
    {
        PrepareScriptAction(uid, agent, frame, CMUUtilityScriptActionKind.Release);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-pull-stop");
    }

    private CMUUtilityScriptActionState PrepareScriptAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        CMUUtilityScriptActionKind kind,
        EntityUid? target = null)
    {
        var now = _timing.CurTime;
        var action = new CMUUtilityScriptActionState(kind)
        {
            Target = target,
            Started = now,
        };

        frame.Instructions = 0;
        frame.Action = action;
        agent.StepDeadline = now + TimeSpan.FromSeconds(30);
        agent.PathAttempts = 0;
        agent.Destination = null;
        _steering.Unregister(uid);
        return action;
    }

    private static void StartSay(CMUUtilityScriptFrame frame, string text)
    {
        frame.Instructions = 0;
        frame.Action = new CMUUtilityScriptActionState(CMUUtilityScriptActionKind.Say)
        {
            Text = text,
        };
    }

    private void FailScriptAction(CMUUtilityScriptFrame frame, string key)
    {
        frame.Failure = Loc.GetString(key);
    }

    private void FailScript(EntityUid uid, CMUUtilityDroneAgentComponent agent, string failure)
    {
        agent.ProgramFailures++;
        StopRuntime(uid, agent, CMUUtilityDroneStatus.Failed);
        agent.Activity = failure;
        agent.LastFailure = failure;
        SetScriptExecution(uid, null);
    }

    private void CompleteScript(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        _steering.Unregister(uid);
        agent.ProgramRunning = false;
        agent.ProgramPaused = false;
        agent.Status = CMUUtilityDroneStatus.Idle;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-complete");
        agent.ProgramsCompleted++;
        SetScriptExecution(uid, null);
    }

    private bool TryResolveActionTarget(CMUUtilityScriptFrame frame, out EntityUid target)
    {
        target = default;
        if (frame.Action?.Target is not { } value ||
            TerminatingOrDeleted(value))
        {
            return false;
        }

        target = value;
        return true;
    }

    private bool TryResolveBackpackItem(
        EntityUid backpack,
        CMUUtilityScriptFrame frame,
        out EntityUid item)
    {
        item = default;
        var action = frame.Action;
        if (action?.Target is { } entity && !TerminatingOrDeleted(entity))
        {
            item = entity;
            return true;
        }

        var query = action?.Text;
        if (string.IsNullOrWhiteSpace(query))
            return false;

        return TryFindBackpackItem(backpack, query, out item);
    }

    private void ExecuteCall(
        EntityUid uid,
        CMUUtilityScriptFrame frame,
        string name,
        int argCount)
    {
        if (argCount < 0 || argCount > 4)
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-function", ("name", name));
            return;
        }

        var args = new CMUUtilityScriptValue[argCount];
        for (var i = argCount - 1; i >= 0; i--)
        {
            if (!TryPop(frame, out args[i]))
                return;
        }

        var value = name switch
        {
            "marker" => NativeMarker(frame, args),
            "hand" => NativeHand(uid, args),
            "activehand" => _hands.TryGetActiveItem(uid, out var active)
                ? CMUUtilityScriptValue.FromEntity(active.Value)
                : CMUUtilityScriptValue.Null,
            "equipped" => NativeEquipped(uid, args),
            "hands" => NativeHands(uid, args),
            "worn" => NativeWorn(uid, args),
            "backpack" => NativeBackpack(uid, args),
            "backpackitem" => NativeBackpackItem(uid, args),
            "backpacknames" => NativeBackpackNames(uid, args),
            "backpackitems" => NativeBackpackItems(uid, args),
            "item" => NativeItem(uid, args),
            "person" => NativePerson(uid, args),
            "nearestperson" => NativeNearestPerson(uid, args),
            "peoplenames" => NativePeopleNames(uid, args),
            "inspectperson" => NativeInspectPerson(uid, args),
            "worncontains" => NativeWornContains(uid, args),
            "people" => NativeNearbyInfo(uid, args, CMUUtilityNearbyKind.People),
            "items" => NativeNearbyInfo(uid, args, CMUUtilityNearbyKind.Items),
            "entities" => NativeNearbyInfo(uid, args, CMUUtilityNearbyKind.Entities),
            "peoplecount" or "personcount" => CMUUtilityScriptValue.FromNumber(CountPeople(uid, args)),
            "itemcount" => CMUUtilityScriptValue.FromNumber(CountItems(uid, args)),
            "backpackcount" => CMUUtilityScriptValue.FromNumber(CountBackpackItems(uid)),
            "name" => NativeName(args),
            "itemname" => NativeName(args),
            "itemid" => NativeItemId(args),
            "itemdisplay" => NativeItemDisplay(uid, args),
            "handname" => NativeHandName(uid, args),
            "inhand" => NativeInHand(uid, args),
            "hasbackpackitem" => NativeHasBackpackItem(uid, args),
            "distance" => NativeDistance(uid, args),
            "damage" => NativeDamage(uid, args),
            "health" => NativeHealth(uid, args),
            "maxhealth" => NativeMaxHealth(uid, args),
            "healthpercent" => NativeHealthPercent(uid, args),
            "alive" => NativeAlive(uid, args),
            "dead" => NativeDead(uid, args),
            "critical" => NativeCritical(uid, args),
            "nearesthostile" => NativeNearestHostile(uid, args),
            "hostiles" => NativeHostiles(uid, args),
            "hostilecount" => CMUUtilityScriptValue.FromNumber(CountHostiles(uid, args)),
            "nearestally" => NativeNearestAlly(uid, args),
            "ammo" => NativeAmmo(uid, args),
            "maxammo" => NativeMaxAmmo(uid, args),
            "incombat" => NativeInCombat(uid, args),
            "pulling" => NativePulling(uid, args),
            "aimtarget" => NativeAimTarget(uid, args),
            "exists" => NativeExists(args),
            "attacker" => NativeAttacker(uid, frame, args),
            "damagetaken" => NativeDamageTaken(uid, args),
            "heardtext" => CMUUtilityScriptValue.FromString(
                frame.EventContext ? frame.EventText : string.Empty),
            "heardspeaker" => frame.EventContext &&
                              frame.EventSubject is { } speaker &&
                              speaker.IsValid() &&
                              !TerminatingOrDeleted(speaker)
                ? CMUUtilityScriptValue.FromEntity(speaker)
                : CMUUtilityScriptValue.Null,
            "backpackfits" => NativeBackpackFits(uid, args),
            _ => CMUUtilityScriptValue.Null,
        };

        Push(frame, value);
    }

}