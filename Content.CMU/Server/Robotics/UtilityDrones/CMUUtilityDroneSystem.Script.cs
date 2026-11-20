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
    /// <summary>
    /// Validates a text program and snapshots the console marker registry into
    /// the board. Marker numbers in the script are one-based. Legacy step and
    /// rule validation is intentionally absent: those fields are ignored for
    /// text boards and remain owned by <see cref="ValidateProgram"/>.
    /// </summary>
    public bool ValidateScript(
        EntityUid console,
        string? source,
        out CMUUtilityScriptProgram program,
        out List<EntityUid> markers,
        out string error)
    {
        program = new CMUUtilityScriptProgram();
        markers = new List<EntityUid>();
        error = string.Empty;

        if (!TryComp<CMUUtilityDroneConsoleComponent>(console, out var consoleComp))
        {
            error = Loc.GetString("cmu-utility-drone-error-controller");
            return false;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            error = Loc.GetString("cmu-utility-drone-script-error-empty");
            return false;
        }

        if (!_scriptCompiler.TryCompile(source, out var compiled, out error))
            return false;

        program = compiled;
        if (!compiled.UsesMarkers)
            return true;

        var ordered = new List<EntityUid>(consoleComp.Markers);
        ordered.Sort((a, b) => GetNetEntity(a).Id.CompareTo(GetNetEntity(b).Id));
        if (ordered.Count == 0)
        {
            error = Loc.GetString("cmu-utility-drone-script-error-no-markers");
            return false;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var marker = ordered[i];
            if (!TryValidateMarker(console, marker, i + 1, out error))
                return false;

            markers.Add(marker);
        }

        return true;
    }

    private bool ValidateBoardScript(
        CMUUtilityDroneProgramBoardComponent board,
        out string error)
    {
        if (!_scriptCompiler.TryCompile(board.Script, out _, out error))
            return false;

        foreach (var marker in board.ScriptMarkers)
        {
            if (TerminatingOrDeleted(marker) ||
                !HasComp<CMUUtilityTaskMarkerComponent>(marker) ||
                !Transform(marker).Anchored)
            {
                error = Loc.GetString("cmu-utility-drone-script-fail-target");
                return false;
            }
        }

        return true;
    }

    private bool StartScriptProgram(
        EntityUid drone,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityDroneProgramBoardComponent board,
        out string error)
    {
        if (!ValidateBoardScript(board, out error) ||
            !_scriptCompiler.TryCompile(board.Script, out var compiled, out error))
        {
            return false;
        }

        var now = _timing.CurTime;
        _steering.Unregister(drone);
        // Text programs never carry legacy steps or rules; the board source is
        // the only program definition for this execution path.
        agent.Program = new CMUUtilityProgram();
        agent.ProgramRunning = true;
        agent.ProgramPaused = false;
        agent.StepIndex = 0;
        agent.StepPhase = 0;
        agent.Waypoint = 0;
        agent.PathAttempts = 0;
        agent.StepStarted = now;
        agent.StepDeadline = now + TimeSpan.FromSeconds(30);
        agent.WaitUntil = TimeSpan.Zero;
        agent.RepeatAt = TimeSpan.Zero;
        agent.WorkStarted = TimeSpan.Zero;
        agent.NextRetry = TimeSpan.Zero;
        agent.PendingTrigger = null;
        agent.PendingSubject = null;
        agent.PendingTriggerAfter = null;
        agent.PendingSubjectAfter = null;
        agent.LastAttacker = null;
        agent.DamagePending = false;
        agent.CombatTarget = null;
        agent.CombatCoordinates = null;
        agent.CombatWeapon = null;
        agent.CombatAbort = false;
        agent.CombatUntil = TimeSpan.Zero;
        agent.WeaponsHold = false;
        agent.ForcedReturn = false;
        agent.StopAfterReturn = false;
        agent.FetchedItem = null;
        agent.WorkTarget = null;
        agent.Destination = null;
        agent.NextReportBySubject.Clear();
        agent.LastPulledEntity = _pulling.GetPulling(drone);
        agent.Status = CMUUtilityDroneStatus.Running;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-script-running");
        agent.NextThink = now;
        agent.NextSense = now;
        agent.NextPullCheck = now;

        var execution = new CMUUtilityScriptExecution(
            compiled,
            markers: new List<EntityUid>(board.ScriptMarkers));
        SetScriptExecution(drone, execution);

        EnsureNpcMovement(drone);
        EnsureComp<ActiveNPCComponent>(drone);
        return true;
    }

    private void EnsureNpcMovement(EntityUid drone)
    {
        EnsureComp<InputMoverComponent>(drone);
        EnsureComp<MobMoverComponent>(drone);
    }

    private void ThinkScript(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        var execution = GetScriptExecution(uid);
        if (execution == null)
        {
            StopRuntime(uid, agent, CMUUtilityDroneStatus.Idle);
            return;
        }

        var frame = execution.Main;
        if (frame.Failed)
        {
            FailScript(uid, agent, frame.Failure);
            return;
        }

        FlushEventSpeech(uid, agent, execution);
        var budget = CMUUtilityDroneLimits.MaxScriptInstructionsPerTick;
        if (execution.ActiveHandler == null)
            ProcessScriptEvents(uid, agent, execution, ref budget);

        if (TickActiveEventHandler(uid, agent, execution, ref budget))
        {
            FlushEventSpeech(uid, agent, execution);
            return;
        }

        FlushEventSpeech(uid, agent, execution);

        if (frame.Ended)
        {
            CompleteScript(uid, agent);
            return;
        }

        while (budget-- > 0)
        {
            if (frame.Action is { } runningAction)
            {
                if (TickScriptAction(uid, agent, frame, runningAction))
                {
                    if (frame.Failed)
                        FailScript(uid, agent, frame.Failure);

                    return;
                }

                continue;
            }

            if (frame.Instructions >= CMUUtilityDroneLimits.MaxScriptInstructions)
            {
                FailScript(uid, agent, Loc.GetString("cmu-utility-drone-script-fail-instructions",
                    ("max", CMUUtilityDroneLimits.MaxScriptInstructions)));
                return;
            }

            if (frame.Pc < 0 || frame.Pc >= frame.Program.Instructions.Count)
            {
                CompleteScript(uid, agent);
                return;
            }

            var instruction = frame.Program.Instructions[frame.Pc];
            frame.Instructions++;
            frame.Pc++;
            ExecuteScriptInstruction(uid, agent, frame, instruction);

            if (frame.Failed)
            {
                FailScript(uid, agent, frame.Failure);
                return;
            }

            if (frame.Action != null)
                return;

            if (frame.Ended)
            {
                CompleteScript(uid, agent);
                return;
            }

            if (frame.Pc < 0 || frame.Pc > frame.Program.Instructions.Count)
            {
                FailScript(uid, agent, Loc.GetString("cmu-utility-drone-script-fail-jump"));
                return;
            }

            if (frame.Pc == frame.Program.Instructions.Count)
            {
                CompleteScript(uid, agent);
                return;
            }
        }
    }

    private void ProcessScriptEvents(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptExecution execution,
        ref int budget)
    {
        var processed = 0;
        while (execution.Events.Count > 0 && processed++ < 4 && budget > 0)
        {
            var scriptEvent = execution.Events.Dequeue();
            if (!TryGetScriptEventHandler(execution.Program, scriptEvent, out var handler))
                continue;

            if (handler.HasBlockingInstructions)
            {
                StartActiveEventHandler(execution, handler, scriptEvent);
                return;
            }

            RunNonBlockingEventHandler(uid, agent, execution, handler, scriptEvent, ref budget);
        }
    }

    private static bool TryGetScriptEventHandler(
        CMUUtilityScriptProgram program,
        CMUUtilityScriptEvent scriptEvent,
        out CMUUtilityScriptHandler handler)
    {
        foreach (var candidate in program.EventHandlers)
        {
            if (candidate.Trigger != scriptEvent.Trigger)
                continue;

            if (scriptEvent.Trigger == CMUUtilityTrigger.OnHear &&
                !string.IsNullOrWhiteSpace(candidate.Phrase) &&
                !scriptEvent.Text.Contains(
                    candidate.Phrase,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            handler = candidate;
            return true;
        }

        handler = default!;
        return false;
    }

    private static void StartActiveEventHandler(
        CMUUtilityScriptExecution execution,
        CMUUtilityScriptHandler handler,
        CMUUtilityScriptEvent scriptEvent)
    {
        execution.ActiveHandler = CreateHandlerFrame(
            execution,
            handler,
            scriptEvent);
    }

    private void RunNonBlockingEventHandler(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptExecution execution,
        CMUUtilityScriptHandler handler,
        CMUUtilityScriptEvent scriptEvent,
        ref int budget)
    {
        var handlerFrame = CreateHandlerFrame(execution, handler, scriptEvent);
        var handlerBudget = Math.Min(
            CMUUtilityDroneLimits.MaxEventHandlerInstructions,
            budget);
        while (handlerFrame.Pc < handler.Program.Instructions.Count &&
               handlerBudget-- > 0 &&
               budget > 0 &&
               !handlerFrame.Failed &&
               !handlerFrame.Ended)
        {
            var instruction = handler.Program.Instructions[handlerFrame.Pc++];
            handlerFrame.Instructions++;
            budget--;
            ExecuteScriptInstruction(uid, agent, handlerFrame, instruction);
        }

        ApplyHandlerEffects(uid, execution, handlerFrame);
    }

    private static CMUUtilityScriptFrame CreateHandlerFrame(
        CMUUtilityScriptExecution execution,
        CMUUtilityScriptHandler handler,
        CMUUtilityScriptEvent scriptEvent)
    {
        return new CMUUtilityScriptFrame(execution, handler.Program)
        {
            EventContext = true,
            EventSubject = scriptEvent.Subject,
            EventText = scriptEvent.Text,
        };
    }

    private bool TickActiveEventHandler(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptExecution execution,
        ref int budget)
    {
        var handlerFrame = execution.ActiveHandler;
        if (handlerFrame == null)
            return false;

        if (handlerFrame.Failed)
        {
            execution.ActiveHandler = null;
            return false;
        }

        if (handlerFrame.Action is { } handlerAction)
        {
            if (TickScriptAction(uid, agent, handlerFrame, handlerAction))
            {
                if (handlerFrame.Failed)
                    execution.ActiveHandler = null;

                return true;
            }
        }

        var handlerBudget = Math.Min(
            CMUUtilityDroneLimits.MaxEventHandlerInstructions,
            budget);
        while (handlerFrame.Pc < handlerFrame.Program.Instructions.Count &&
               handlerBudget-- > 0 &&
               budget > 0 &&
               !handlerFrame.Failed &&
               !handlerFrame.Ended)
        {
            var instruction = handlerFrame.Program.Instructions[handlerFrame.Pc++];
            handlerFrame.Instructions++;
            budget--;
            ExecuteScriptInstruction(uid, agent, handlerFrame, instruction);

            if (handlerFrame.Action != null)
                return true;
        }

        if (!handlerFrame.Ended &&
            !handlerFrame.Failed &&
            handlerFrame.Pc < handlerFrame.Program.Instructions.Count)
        {
            return true;
        }

        execution.ActiveHandler = null;
        ApplyHandlerEffects(uid, execution, handlerFrame);
        return false;
    }

    private void JumpMainRuntime(
        EntityUid uid,
        CMUUtilityScriptFrame mainFrame,
        int programCounter)
    {
        mainFrame.Pc = programCounter;
        mainFrame.Instructions = 0;
        mainFrame.Action = null;
        mainFrame.Ended = false;
        _steering.Unregister(uid);
    }

    private void ApplyHandlerEffects(
        EntityUid uid,
        CMUUtilityScriptExecution execution,
        CMUUtilityScriptFrame handlerFrame)
    {
        var mainFrame = execution.Main;
        if (handlerFrame.RestartRequested)
        {
            JumpMainRuntime(uid, mainFrame, mainFrame.Program.MainStart);
        }
        else if (!string.IsNullOrWhiteSpace(handlerFrame.GotoMainLabel))
        {
            if (mainFrame.Program.Labels.TryGetValue(
                    handlerFrame.GotoMainLabel,
                    out var labelPc))
            {
                JumpMainRuntime(uid, mainFrame, labelPc);
            }
            else
            {
                mainFrame.Failure = Loc.GetString(
                    "cmu-utility-drone-script-fail-label",
                    ("label", handlerFrame.GotoMainLabel));
            }
        }

        if (handlerFrame.StopRequested)
        {
            mainFrame.Action = null;
            mainFrame.Ended = true;
            _steering.Unregister(uid);
        }
    }

    private void FlushEventSpeech(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptExecution execution)
    {
        if (execution.EventSpeech.Count == 0 || _timing.CurTime < agent.NextChat)
            return;

        var text = execution.EventSpeech.Dequeue();
        if (string.IsNullOrWhiteSpace(text))
            return;

        _chat.TrySendInGameICMessage(
            uid,
            text,
            InGameICChatType.Speak,
            false,
            ignoreActionBlocker: true);
        agent.NextChat = _timing.CurTime + TimeSpan.FromSeconds(1);
    }

    private void ExecuteScriptInstruction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        CMUUtilityScriptInstruction instruction)
    {
        switch (instruction.Op)
        {
            case CMUUtilityScriptOp.PushNumber:
                Push(frame, CMUUtilityScriptValue.FromNumber(instruction.Number));
                break;
            case CMUUtilityScriptOp.PushString:
                Push(frame, CMUUtilityScriptValue.FromString(instruction.Text));
                break;
            case CMUUtilityScriptOp.PushBool:
                Push(frame, CMUUtilityScriptValue.FromBool(instruction.Number != 0));
                break;
            case CMUUtilityScriptOp.LoadVariable:
                Push(frame, frame.Variables.GetValueOrDefault(instruction.Text, CMUUtilityScriptValue.Null));
                break;
            case CMUUtilityScriptOp.StoreVariable:
                if (!TryPop(frame, out var stored))
                    return;
                frame.Variables[instruction.Text] = stored;
                break;
            case CMUUtilityScriptOp.Binary:
                CMUUtilityScriptVm.ExecuteBinary(frame, instruction.Text);
                break;
            case CMUUtilityScriptOp.Unary:
                CMUUtilityScriptVm.ExecuteUnary(frame, instruction.Text);
                break;
            case CMUUtilityScriptOp.Call:
                ExecuteCall(uid, frame, instruction.Text, instruction.ArgCount);
                break;
            case CMUUtilityScriptOp.Jump:
                frame.Pc = instruction.Jump;
                break;
            case CMUUtilityScriptOp.JumpIfFalse:
                if (!TryPop(frame, out var condition))
                    return;
                if (!condition.IsTruthy)
                    frame.Pc = instruction.Jump;
                break;
            case CMUUtilityScriptOp.Say:
                if (!TryPop(frame, out var said))
                    return;
                if (frame.EventContext)
                    frame.Execution.EnqueueEventSpeech(said.AsText);
                else
                    StartSay(frame, said.AsText);
                break;
            case CMUUtilityScriptOp.Wait:
                if (!TryPopNumber(frame, out var seconds))
                    return;
                if (seconds < 0)
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-value");
                    return;
                }

                frame.Action = new CMUUtilityScriptActionState(CMUUtilityScriptActionKind.Wait)
                {
                    Started = _timing.CurTime,
                    WaitUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Min(seconds, 600)),
                };
                frame.Instructions = 0;
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-waiting");
                _steering.Unregister(uid);
                break;
            case CMUUtilityScriptOp.MoveEntity:
                if (!TryPopEntity(frame, out var moveTarget))
                    return;
                StartMoveAction(uid, agent, frame, moveTarget);
                break;
            case CMUUtilityScriptOp.Follow:
                if (!TryPopNumber(frame, out var followSeconds))
                    return;
                StartFollowAction(uid, agent, frame, Math.Max(0, followSeconds));
                break;
            case CMUUtilityScriptOp.Patrol:
                if (!TryPopNumber(frame, out var patrolSeconds) ||
                    !TryPopNumber(frame, out var patrolRadius) ||
                    !TryPopEntity(frame, out var patrolMarker))
                {
                    return;
                }

                StartPatrolAction(uid, agent, frame, patrolMarker, (float) patrolRadius, (float) patrolSeconds);
                break;
            case CMUUtilityScriptOp.Return:
                StartReturnAction(uid, agent, frame);
                break;
            case CMUUtilityScriptOp.TakeEntity:
                if (!TryPop(frame, out var takeValue))
                    return;
                if (!TryResolveItemValue(uid, takeValue, out var takeTarget))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-item");
                    return;
                }

                StartTakeAction(uid, agent, frame, takeTarget, instruction.Text);
                break;
            case CMUUtilityScriptOp.TakeBackpack:
                if (!TryPop(frame, out var backpackValue))
                    return;
                StartTakeBackpackAction(uid, agent, frame, backpackValue, instruction.Text);
                break;
            case CMUUtilityScriptOp.PutBackpack:
                if (!TryPop(frame, out var putValue))
                    return;
                StartPutBackpackAction(uid, agent, frame, putValue);
                break;
            case CMUUtilityScriptOp.Pull:
                if (!TryPop(frame, out var pullValue))
                    return;
                if (!TryResolveItemValue(uid, pullValue, out var pullTarget))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-item");
                    return;
                }

                StartPullAction(uid, agent, frame, pullTarget);
                break;
            case CMUUtilityScriptOp.Release:
                StartReleaseAction(uid, agent, frame);
                break;
            case CMUUtilityScriptOp.SwitchHand:
                if (!TryPop(frame, out var handValue))
                    return;
                SwitchHand(uid, handValue.Text);
                break;
            case CMUUtilityScriptOp.Drop:
                _hands.TryDrop(uid);
                break;
            case CMUUtilityScriptOp.MoveCoordinates:
                if (!TryPopNumber(frame, out var coordinateY) ||
                    !TryPopNumber(frame, out var coordinateX))
                {
                    return;
                }

                StartMoveCoordinatesAction(
                    uid,
                    agent,
                    frame,
                    Transform(uid).Coordinates.WithPosition(
                        new System.Numerics.Vector2((float) coordinateX, (float) coordinateY)));
                break;
            case CMUUtilityScriptOp.MoveDirection:
                if (!TryPopNumber(frame, out var directionDistance) ||
                    !TryPop(frame, out var directionValue) ||
                    directionValue.Type != CMUUtilityScriptValueType.String)
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-value");
                    return;
                }

                var direction = directionValue.Text.ToLowerInvariant();
                var directionOffset = direction switch
                {
                    "up" or "north" => new System.Numerics.Vector2(0, 1),
                    "down" or "south" => new System.Numerics.Vector2(0, -1),
                    "right" or "east" => new System.Numerics.Vector2(1, 0),
                    "left" or "west" => new System.Numerics.Vector2(-1, 0),
                    _ => System.Numerics.Vector2.Zero,
                };
                if (directionOffset == System.Numerics.Vector2.Zero ||
                    directionDistance <= 0)
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-value");
                    return;
                }

                StartMoveCoordinatesAction(
                    uid,
                    agent,
                    frame,
                    Transform(uid).Coordinates.WithPosition(
                        Transform(uid).Coordinates.Position +
                        directionOffset * (float) directionDistance));
                break;
            case CMUUtilityScriptOp.UseSelf:
                if (!_hands.TryGetActiveItem(uid, out var selfUseItem))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-hand");
                    return;
                }

                _interaction.UseInHandInteraction(uid, selfUseItem.Value);
                break;
            case CMUUtilityScriptOp.UseActiveOnTarget:
                if (!TryPop(frame, out var activeTargetValue))
                    return;
                if (!TryResolveInteractionTarget(uid, activeTargetValue, out var activeTarget))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
                    return;
                }

                if (!_hands.TryGetActiveItem(uid, out var activeUseItem))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-hand");
                    return;
                }

                StartUseAction(uid, agent, frame, activeUseItem.Value, activeTarget);
                break;
            case CMUUtilityScriptOp.UseItemOnTarget:
                if (!TryPop(frame, out var explicitTargetValue) ||
                    !TryPop(frame, out var explicitItemValue))
                {
                    return;
                }

                if (!TryResolveInteractionTarget(uid, explicitTargetValue, out var explicitTarget))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
                    return;
                }

                if (!TryResolveHeldItem(uid, explicitItemValue, out var explicitItem))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-item");
                    return;
                }

                StartUseAction(uid, agent, frame, explicitItem, explicitTarget);
                break;
            case CMUUtilityScriptOp.Wield:
                if (!TryPop(frame, out var wieldValue))
                    return;

                if (!TryWieldScriptItem(uid, agent, wieldValue, out var wieldFailure))
                    frame.Failure = Loc.GetString(wieldFailure);

                break;
            case CMUUtilityScriptOp.Unwield:
                UnwieldScriptWeapon(uid, agent);
                break;
            case CMUUtilityScriptOp.AimEntity:
                if (!TryPop(frame, out var aimValue))
                    return;

                if (!SetScriptAimEntity(uid, agent, aimValue))
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");

                break;
            case CMUUtilityScriptOp.AimCoordinates:
                if (!TryPopNumber(frame, out var aimY) ||
                    !TryPopNumber(frame, out var aimX))
                {
                    return;
                }

                SetScriptAimCoordinates(uid, agent, (float) aimX, (float) aimY);
                break;
            case CMUUtilityScriptOp.AimClear:
                ClearScriptAim(uid, agent);
                break;
            case CMUUtilityScriptOp.Shoot:
                if (!TryPopNumber(frame, out var shootCount))
                    return;

                StartShootScriptAction(uid, agent, frame, null, null, (int) shootCount);
                break;
            case CMUUtilityScriptOp.ShootAt:
                if (!TryPopNumber(frame, out var shootAtCount) ||
                    !TryPop(frame, out var shootAtValue))
                {
                    return;
                }

                if (!TryResolveCombatTarget(uid, shootAtValue, out var shootAtTarget))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
                    return;
                }

                StartShootScriptAction(
                    uid,
                    agent,
                    frame,
                    shootAtTarget,
                    null,
                    (int) shootAtCount);
                break;
            case CMUUtilityScriptOp.Reload:
                if (!ReloadScriptWeapon(uid, out var reloadFailure))
                    frame.Failure = Loc.GetString(reloadFailure);

                break;
            case CMUUtilityScriptOp.HoldFire:
                agent.WeaponsHold = true;
                break;
            case CMUUtilityScriptOp.FreeFire:
                agent.WeaponsHold = false;
                break;
            case CMUUtilityScriptOp.Fight:
                if (!TryPopNumber(frame, out var fightRadius))
                    return;

                EntityUid? fightTarget = null;
                if (!string.Equals(instruction.Text, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryPop(frame, out var fightValue) ||
                        !TryResolveCombatTarget(uid, fightValue, out var resolvedFightTarget))
                    {
                        frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
                        return;
                    }

                    fightTarget = resolvedFightTarget;
                }

                StartFightAction(
                    uid,
                    agent,
                    frame,
                    fightTarget,
                    (float) fightRadius);
                break;
            case CMUUtilityScriptOp.Disengage:
                DisengageScriptCombat(uid, agent);
                break;
            case CMUUtilityScriptOp.Repair:
                if (!TryPopNumber(frame, out var repairRadius))
                    return;

                EntityUid? repairTarget = null;
                if (!string.Equals(instruction.Text, "target", StringComparison.OrdinalIgnoreCase))
                {
                    StartRepairAction(
                        uid,
                        agent,
                        frame,
                        instruction.Text,
                        null,
                        (float) repairRadius);
                    break;
                }

                if (!TryPop(frame, out var repairValue) ||
                    !TryResolveCombatTarget(uid, repairValue, out var resolvedRepairTarget))
                {
                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
                    return;
                }

                repairTarget = resolvedRepairTarget;
                StartRepairAction(
                    uid,
                    agent,
                    frame,
                    instruction.Text,
                    repairTarget,
                    (float) repairRadius);
                break;
            case CMUUtilityScriptOp.Goto:
                var gotoLabel = instruction.Text;
                if (frame.EventContext &&
                    gotoLabel.StartsWith("main.", StringComparison.OrdinalIgnoreCase))
                {
                    frame.GotoMainLabel = gotoLabel["main.".Length..];
                    frame.Ended = true;
                    break;
                }

                if (gotoLabel.StartsWith("main.", StringComparison.OrdinalIgnoreCase))
                    gotoLabel = gotoLabel["main.".Length..];

                if (!frame.Program.Labels.TryGetValue(gotoLabel, out var labelPc))
                {
                    frame.Failure = Loc.GetString(
                        "cmu-utility-drone-script-fail-label",
                        ("label", gotoLabel));
                    break;
                }

                frame.Pc = labelPc;
                break;
            case CMUUtilityScriptOp.HandlerReturn:
                frame.Ended = true;
                break;
            case CMUUtilityScriptOp.Restart:
                if (frame.EventContext)
                {
                    frame.RestartRequested = true;
                    frame.Ended = true;
                    break;
                }

                frame.Pc = frame.Program.MainStart;
                frame.Instructions = 0;
                frame.Action = null;
                frame.Ended = false;
                _steering.Unregister(uid);
                break;
            case CMUUtilityScriptOp.Stop:
                if (frame.EventContext)
                    frame.StopRequested = true;

                frame.Ended = true;
                break;
            case CMUUtilityScriptOp.End:
                frame.Ended = true;
                break;
        }
    }

}