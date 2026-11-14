using Content.Shared.Chat;
using Content.Shared.CMU14.Robotics.UtilityDrones;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private const float StepTimeoutSeconds = 30f;
    private const float ReturnTimeoutSeconds = 120f;

    private void Think(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        if (!IsAlive(uid))
        {
            StopRuntime(uid, agent, CMUUtilityDroneStatus.Failed);
            ClearHomeAnchor(agent);
            return;
        }

        if (agent.ProgramBoard is not { } board ||
            TerminatingOrDeleted(board) ||
            !HasComp<CMUUtilityDroneProgramBoardComponent>(board))
        {
            StopRuntime(uid, agent, CMUUtilityDroneStatus.Failed);
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-board-missing");
            ClearHomeAnchor(agent);
            return;
        }

        if (agent.ForcedReturn)
        {
            ThinkForcedReturn(uid, agent);
            return;
        }

        if (!agent.ProgrammableMode)
        {
            agent.ProgramPaused = true;
            agent.Status = CMUUtilityDroneStatus.Paused;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-paused");
            _steering.Unregister(uid);
            return;
        }

        if (!agent.ProgramRunning)
        {
            _steering.Unregister(uid);
            if (agent.Status == CMUUtilityDroneStatus.Failed)
                return;

            agent.Status = CMUUtilityDroneStatus.Idle;
            agent.Activity = string.Empty;
            return;
        }

        if (agent.ProgramPaused)
        {
            agent.Status = CMUUtilityDroneStatus.Paused;
            _steering.Unregister(uid);
            return;
        }

        var scriptExecution = GetScriptExecution(uid);
        if (scriptExecution == null)
        {
            if (agent.NextPullCheck <= _timing.CurTime)
            {
                agent.NextPullCheck = _timing.CurTime + TimeSpan.FromSeconds(0.5);
                CheckPullTriggers(uid, agent);
            }

            if (ProcessPendingTriggers(uid, agent))
                return;

            if (HasSeeRule(agent) && agent.NextSense <= _timing.CurTime)
                SenseForRule(uid, agent);

            if (agent.ForcedReturn)
                return;
        }

        if (scriptExecution != null)
        {
            ThinkScript(uid, agent);
            return;
        }

        if (agent.Program.Steps.Count == 0)
        {
            StopRuntime(uid, agent, CMUUtilityDroneStatus.Idle);
            return;
        }

        if (agent.RepeatAt != TimeSpan.Zero)
        {
            if (_timing.CurTime < agent.RepeatAt)
            {
                agent.Status = CMUUtilityDroneStatus.Running;
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-repeat-wait");
                _steering.Unregister(uid);
                return;
            }

            agent.RepeatAt = TimeSpan.Zero;
            agent.StepStarted = _timing.CurTime;
            agent.StepDeadline = _timing.CurTime + TimeSpan.FromSeconds(StepTimeoutSeconds);
        }

        if (agent.StepIndex < 0 || agent.StepIndex >= agent.Program.Steps.Count)
            agent.StepIndex = 0;

        if (_timing.CurTime > agent.StepDeadline)
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-timeout");
            return;
        }

        var step = agent.Program.Steps[agent.StepIndex];
        switch (step.Kind)
        {
            case CMUUtilityStepKind.MoveTo:
                ThinkMoveTo(uid, agent, step);
                break;
            case CMUUtilityStepKind.Guard:
                ThinkGuard(uid, agent, step);
                break;
            case CMUUtilityStepKind.Follow:
                ThinkFollow(uid, agent, step);
                break;
            case CMUUtilityStepKind.Patrol:
                ThinkPatrol(uid, agent, step);
                break;
            case CMUUtilityStepKind.Fetch:
                ThinkFetch(uid, agent, step);
                break;
            case CMUUtilityStepKind.Pull:
                ThinkPull(uid, agent, step);
                break;
            case CMUUtilityStepKind.StopPull:
                ThinkStopPull(uid, agent);
                break;
            case CMUUtilityStepKind.Deliver:
                ThinkDeliver(uid, agent, step);
                break;
            case CMUUtilityStepKind.Repair:
                ThinkRepair(uid, agent, step);
                break;
            case CMUUtilityStepKind.Report:
                ThinkReport(uid, agent, step);
                break;
            case CMUUtilityStepKind.Wait:
                ThinkWait(uid, agent, step);
                break;
            case CMUUtilityStepKind.Return:
                ThinkReturnStep(uid, agent);
                break;
            default:
                FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-invalid-step");
                break;
        }
    }

    private void ThinkForcedReturn(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        if (agent.HomeAnchor is not { } home ||
            TerminatingOrDeleted(home) ||
            _timing.CurTime > agent.ForcedReturnUntil ||
            !SameZNetwork(uid, home))
        {
            if (agent.StopAfterReturn)
            {
                StopRuntime(uid, agent, CMUUtilityDroneStatus.Failed);
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-return-failed");
            }
            else
            {
                agent.ForcedReturn = false;
                agent.ProgramPaused = false;
            }

            return;
        }

        agent.Status = CMUUtilityDroneStatus.Returning;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-returning");
        var move = MoveToEntity(uid, agent, home, 1.8f);
        if (move == MoveResult.Arrived)
        {
            _steering.Unregister(uid);
            if (agent.StopAfterReturn)
            {
                StopRuntime(uid, agent, CMUUtilityDroneStatus.Idle);
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-returned");
            }
            else
            {
                agent.ForcedReturn = false;
                agent.ProgramPaused = false;
                CompleteStep(uid, agent);
            }
        }
        else if (move == MoveResult.Failed)
        {
            if (agent.StopAfterReturn)
            {
                StopRuntime(uid, agent, CMUUtilityDroneStatus.Failed);
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-return-failed");
            }
            else
            {
                FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-return-failed");
            }
        }
    }

    private void ThinkReturnStep(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        if (agent.HomeAnchor is not { } home ||
            TerminatingOrDeleted(home) ||
            !SameZNetwork(uid, home))
        {
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-home-missing");
            return;
        }

        agent.Activity = Loc.GetString("cmu-utility-drone-activity-returning");
        var move = MoveToEntity(uid, agent, home, 1.8f);
        if (move == MoveResult.Arrived)
            CompleteStep(uid, agent);
        else if (move == MoveResult.Failed)
            FailCurrentStep(uid, agent, false, "cmu-utility-drone-activity-return-failed");
    }

    private void ThinkWait(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityStep step)
    {
        _steering.Unregister(uid);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-waiting");
        if (agent.WaitUntil == TimeSpan.Zero)
            agent.WaitUntil = _timing.CurTime + TimeSpan.FromSeconds(step.Seconds);

        if (_timing.CurTime >= agent.WaitUntil)
            CompleteStep(uid, agent);
    }

    private void CompleteStep(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        var now = _timing.CurTime;
        agent.StepsCompleted++;
        agent.StepIndex++;
        agent.StepPhase = 0;
        agent.Waypoint = 0;
        agent.PathAttempts = 0;
        agent.StepStarted = now;
        agent.StepDeadline = now + TimeSpan.FromSeconds(StepTimeoutSeconds);
        agent.WaitUntil = TimeSpan.Zero;
        agent.NextRetry = TimeSpan.Zero;
        agent.WorkStarted = TimeSpan.Zero;
        agent.WorkTarget = null;
        agent.Destination = null;
        _steering.Unregister(uid);

        if (agent.StepIndex < agent.Program.Steps.Count)
        {
            agent.Status = CMUUtilityDroneStatus.Running;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-running");
            return;
        }

        if (agent.Program.Repeat)
        {
            agent.StepIndex = 0;
            agent.RepeatAt = now + TimeSpan.FromSeconds(agent.Program.RepeatCooldown);
            agent.Status = CMUUtilityDroneStatus.Running;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-repeat-wait");
            return;
        }

        agent.ProgramRunning = false;
        agent.ProgramPaused = false;
        agent.Status = CMUUtilityDroneStatus.Idle;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-complete");
        agent.ProgramsCompleted++;
        agent.StepIndex = 0;
        agent.FetchedItem = null;
    }

    private void FailCurrentStep(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        bool lowSupplies,
        string activity)
    {
        agent.ProgramFailures++;
        agent.LastFailure = Loc.GetString(activity);
        _steering.Unregister(uid);

        if (lowSupplies &&
            ApplyRule(uid, agent, CMUUtilityTrigger.OnLowSupplies, null))
        {
            return;
        }

        if (ApplyRule(uid, agent, CMUUtilityTrigger.OnTaskFailed, null))
            return;

        StopRuntime(uid, agent, CMUUtilityDroneStatus.Failed);
        agent.Activity = Loc.GetString(activity);
    }

    private bool ApplyRule(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityTrigger trigger,
        EntityUid? subject)
    {
        foreach (var rule in agent.Program.Rules)
        {
            if (rule.Trigger != trigger)
                continue;

            if (rule.Action == CMUUtilityRuleAction.Return)
            {
                if (!agent.ForcedReturn)
                    StartReturn(uid, agent, stopAfterReturn: true);
                return true;
            }

            if (rule.Action == CMUUtilityRuleAction.Report)
            {
                TryReport(uid, agent, subject, rule.Message);
                return trigger == CMUUtilityTrigger.OnSee;
            }
        }

        return false;
    }

    private void StartReturn(EntityUid uid, CMUUtilityDroneAgentComponent agent, bool stopAfterReturn)
    {
        agent.ForcedReturn = true;
        agent.StopAfterReturn = stopAfterReturn;
        agent.ForcedReturnUntil = _timing.CurTime + TimeSpan.FromSeconds(ReturnTimeoutSeconds);
        agent.ProgramPaused = true;
        agent.Status = CMUUtilityDroneStatus.Returning;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-returning");
        agent.StepPhase = 0;
        agent.PathAttempts = 0;
        agent.StepDeadline = _timing.CurTime + TimeSpan.FromSeconds(StepTimeoutSeconds);
        _steering.Unregister(uid);
    }

    private void SenseForRule(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        agent.NextSense = _timing.CurTime + TimeSpan.FromSeconds(1);
        if (!TryFindSubject(uid, Transform(uid).Coordinates, agent.SightRange, out var subject))
            return;

        agent.PendingTrigger = CMUUtilityTrigger.OnSee;
        agent.PendingSubject = subject;
    }

    private void CheckPullTriggers(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        var current = _pulling.GetPulling(uid);
        var previous = agent.LastPulledEntity;
        if (current == previous)
            return;

        agent.LastPulledEntity = current;

        if (previous is { } stopped && !TerminatingOrDeleted(stopped))
            QueueTrigger(agent, CMUUtilityTrigger.OnStoppedPulling, stopped);
        else if (previous != null)
            QueueTrigger(agent, CMUUtilityTrigger.OnStoppedPulling, null);

        if (current is { } started)
            QueueTrigger(agent, CMUUtilityTrigger.OnStartedPulling, started);
    }

    private bool ProcessPendingTriggers(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        if (agent.PendingTrigger is { } trigger)
        {
            var subject = agent.PendingSubject;
            agent.PendingTrigger = null;
            agent.PendingSubject = null;
            if (ApplyRule(uid, agent, trigger, subject))
                return true;
        }

        if (agent.PendingTriggerAfter is { } afterTrigger)
        {
            var afterSubject = agent.PendingSubjectAfter;
            agent.PendingTriggerAfter = null;
            agent.PendingSubjectAfter = null;
            if (ApplyRule(uid, agent, afterTrigger, afterSubject))
                return true;
        }

        return false;
    }

    private static void QueueTrigger(
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityTrigger trigger,
        EntityUid? subject)
    {
        if (agent.PendingTrigger == null)
        {
            agent.PendingTrigger = trigger;
            agent.PendingSubject = subject;
            return;
        }

        if (agent.PendingTriggerAfter == null)
        {
            agent.PendingTriggerAfter = trigger;
            agent.PendingSubjectAfter = subject;
        }
    }

    private bool TryReport(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        EntityUid? subject,
        string? message = null)
    {
        var now = _timing.CurTime;
        if (agent.NextChat > now)
            return false;

        var customMessage = !string.IsNullOrWhiteSpace(message);
        if (!customMessage &&
            subject is { } selected &&
            agent.NextReportBySubject.TryGetValue(selected, out var next) &&
            next > now)
        {
            return false;
        }

        var text = customMessage
            ? message!
            : subject is { } target
                ? Loc.GetString("cmu-utility-drone-report-chat",
                    ("subject", MetaData(target).EntityName))
                : Loc.GetString("cmu-utility-drone-report-area-chat");

        _chat.TrySendInGameICMessage(uid, text, InGameICChatType.Speak, false, ignoreActionBlocker: true);
        agent.NextChat = now + TimeSpan.FromSeconds(2);
        agent.ReportsSent++;
        agent.ReportSubject = subject;

        if (!customMessage && subject is { } reported)
        {
            agent.NextReportBySubject[reported] = now + TimeSpan.FromSeconds(10);
            PruneReportCooldowns(agent, now);
        }

        return true;
    }

    private static void PruneReportCooldowns(CMUUtilityDroneAgentComponent agent, TimeSpan now)
    {
        if (agent.NextReportBySubject.Count <= 64)
            return;

        var stale = new List<EntityUid>();
        foreach (var (subject, next) in agent.NextReportBySubject)
        {
            if (next <= now)
                stale.Add(subject);
        }

        foreach (var subject in stale)
            agent.NextReportBySubject.Remove(subject);
    }

    private static bool HasSeeRule(CMUUtilityDroneAgentComponent agent)
    {
        foreach (var rule in agent.Program.Rules)
        {
            if (rule.Trigger == CMUUtilityTrigger.OnSee)
                return true;
        }

        return false;
    }
}
