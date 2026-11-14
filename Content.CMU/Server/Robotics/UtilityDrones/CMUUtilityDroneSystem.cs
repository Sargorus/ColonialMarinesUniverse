using Content.Server.Chat.Systems;
using Content.Server.CMU14.DroneOperator;
using Content.Server.CMU14.ZLevels.Core;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._RMC14.Input;
using Content.Shared._RMC14.Repairable;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Tools;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Timing;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Authoritative utility-drone runtime: board installation, bounded thinking,
/// program execution and physical Z-level traversal. It never selects combat
/// targets and never spawns or teleports drones.
/// </summary>
public sealed partial class CMUUtilityDroneSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private CMUDroneOperatorSystem _droneOperator = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemToggleSystem _itemToggle = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private SharedWieldableSystem _wieldable = default!;
    [Dependency] private CMUZLevelsSystem _zLevels = default!;

    private readonly HashSet<EntityUid> _lookupBuffer = new();
    private readonly CMUUtilityScriptCompiler _scriptCompiler = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, ComponentStartup>(OnAgentStartup);
        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, MapInitEvent>(OnAgentMapInit);
        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, ComponentShutdown>(OnAgentShutdown);
        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, DamageChangedEvent>(OnAgentDamaged);
        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, ListenEvent>(OnAgentHear);
        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, MobStateChangedEvent>(OnAgentMobState);
        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, InteractUsingEvent>(OnAgentInteractUsing);
        SubscribeLocalEvent<CMUUtilityDroneAgentComponent, CMUUtilityDroneInstallBoardDoAfterEvent>(OnInstallBoardComplete);
    }

    private void OnAgentStartup(Entity<CMUUtilityDroneAgentComponent> ent, ref ComponentStartup args)
    {
        // Remove the player-only input optimization before MapInit can disable
        // NPC movement for a drone without an attached actor.
        RemComp<RMCActiveInputMoverComponent>(ent);
    }

    private void OnAgentMapInit(Entity<CMUUtilityDroneAgentComponent> ent, ref MapInitEvent args)
    {
        EnsureComp<ActiveNPCComponent>(ent);
        EnsureComp<InputMoverComponent>(ent);
        EnsureComp<MobMoverComponent>(ent);
        EnsureComp<ActiveListenerComponent>(ent);
        EnsureComp<CMUUtilityDroneRuntimeComponent>(ent);
        RemComp<NPCMeleeCombatComponent>(ent);
        // Utility drones are strictly non-combat: the civilian parent frame
        // carries a basic melee weapon for other synthetic roles.
        RemComp<MeleeWeaponComponent>(ent);
        // The synthetic species carries a player input mover that disables NPC
        // movement when no client is attached. Utility drones are always server-driven.
        RemComp<RMCActiveInputMoverComponent>(ent);
        ent.Comp.NextThink = _timing.CurTime;
        ent.Comp.Number = ent.Owner.Id;
        EnsureProgramBoardContainer(ent.Owner, ent.Comp);
    }

    private void OnAgentShutdown(Entity<CMUUtilityDroneAgentComponent> ent, ref ComponentShutdown args)
    {
        StopRuntime(ent.Owner, ent.Comp, CMUUtilityDroneStatus.Idle);
        ClearHomeAnchor(ent.Comp);
    }

    private void OnAgentDamaged(Entity<CMUUtilityDroneAgentComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !ent.Comp.ProgramRunning)
            return;

        var attacker = args.Origin is { } origin &&
                       origin.IsValid() &&
                       origin != ent.Owner &&
                       !TerminatingOrDeleted(origin)
            ? origin
            : args.Tool is { } tool &&
              tool.IsValid() &&
              tool != ent.Owner &&
              !TerminatingOrDeleted(tool)
                ? tool
                : (EntityUid?) null;

        ent.Comp.LastAttacker = attacker;
        ent.Comp.DamagePending = true;
        ent.Comp.CombatUntil = _timing.CurTime + TimeSpan.FromSeconds(15);

        if (GetScriptExecution(ent.Owner) is { } execution)
        {
            if (!HasScriptHandler(ent.Owner, CMUUtilityTrigger.OnDamaged))
                return;

            execution.EnqueueEvent(CMUUtilityTrigger.OnDamaged, attacker);
            ent.Comp.NextThink = _timing.CurTime;
            return;
        }

        ent.Comp.PendingTrigger = CMUUtilityTrigger.OnDamaged;
        ent.Comp.PendingSubject = null;
    }

    private void OnAgentHear(
        Entity<CMUUtilityDroneAgentComponent> ent,
        ref ListenEvent args)
    {
        if (!ent.Comp.ProgramRunning ||
            args.Source == ent.Owner ||
            !HasScriptHandler(ent.Owner, CMUUtilityTrigger.OnHear) ||
            GetScriptExecution(ent.Owner) is not { } execution)
        {
            return;
        }

        execution.EnqueueEvent(CMUUtilityTrigger.OnHear, args.Source, args.Message);
        ent.Comp.NextThink = _timing.CurTime;
    }

    private void OnAgentMobState(Entity<CMUUtilityDroneAgentComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        StopRuntime(ent.Owner, ent.Comp, CMUUtilityDroneStatus.Failed);
        ClearHomeAnchor(ent.Comp);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var budget = CMUUtilityDroneLimits.ThinkBudgetPerTick;
        var query = EntityQueryEnumerator<CMUUtilityDroneAgentComponent>();
        while (budget > 0 && query.MoveNext(out var uid, out var agent))
        {
            if (TerminatingOrDeleted(uid) || agent.NextThink > now)
                continue;

            var interval = agent.ThinkInterval > TimeSpan.Zero
                ? agent.ThinkInterval
                : TimeSpan.FromSeconds(0.5);
            agent.NextThink = now + interval;
            budget--;
            Think(uid, agent);
        }
    }

    /// <summary>
    /// Toggles autonomous board execution. Enabling releases any remote
    /// control link first, so a programmable drone cannot be driven manually.
    /// </summary>
    public bool TrySetProgrammableMode(EntityUid drone, bool enabled, out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent))
        {
            error = Loc.GetString("cmu-utility-drone-error-agent");
            return false;
        }

        if (agent.ProgrammableMode == enabled)
            return true;

        if (enabled && CountProgrammableDrones() >= CMUUtilityDroneLimits.MaxDrones)
        {
            error = Loc.GetString("cmu-utility-drone-error-programmable-limit",
                ("limit", CMUUtilityDroneLimits.MaxDrones));
            return false;
        }

        if (enabled)
        {
            _droneOperator.ReleaseDroneFromRemoteControl(
                drone,
                Loc.GetString("cmu-utility-drone-control-ended-programmable"));
        }
        else if (agent.ProgramRunning && !agent.ProgramPaused)
        {
            PauseProgram(drone, out _);
        }

        agent.ProgrammableMode = enabled;
        return true;
    }

    public bool TrySetBoardPortLocked(EntityUid drone, bool locked, out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent))
        {
            error = Loc.GetString("cmu-utility-drone-error-agent");
            return false;
        }

        agent.BoardPortLocked = locked;
        return true;
    }

    /// <summary>
    /// Checks every non-timed installation requirement. The caller still needs
    /// to complete the three-second do-after before the board is inserted.
    /// </summary>
    public bool CanInstallProgramBoard(EntityUid drone, EntityUid board, EntityUid installer, out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent))
        {
            error = Loc.GetString("cmu-utility-drone-error-agent");
            return false;
        }

        if (!IsAlive(drone))
        {
            error = Loc.GetString("cmu-utility-drone-error-agent-dead");
            return false;
        }

        if (!agent.ProgrammableMode)
        {
            error = Loc.GetString("cmu-utility-drone-error-programmable-mode");
            return false;
        }

        if (agent.BoardPortLocked)
        {
            error = Loc.GetString("cmu-utility-drone-error-board-port-locked");
            return false;
        }

        if (!TryComp<CMUUtilityDroneProgramBoardComponent>(board, out var boardComp))
        {
            error = Loc.GetString("cmu-utility-drone-error-board-invalid");
            return false;
        }

        PruneProgramBoard(drone, agent);
        if (agent.ProgramBoard != null || EnsureProgramBoardContainer(drone, agent).ContainedEntity != null)
        {
            error = Loc.GetString("cmu-utility-drone-error-board-slot-full");
            return false;
        }

        if (!_hands.TryGetActiveItem(installer, out var active) || active != board)
        {
            error = Loc.GetString("cmu-utility-drone-error-board-main-hand");
            return false;
        }

        if (!HasMultitoolInOtherHand(installer, board))
        {
            error = Loc.GetString("cmu-utility-drone-error-board-multitool");
            return false;
        }

        if (!IsAlive(installer))
        {
            error = Loc.GetString("cmu-utility-drone-error-installer-dead");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(boardComp.Script))
            return ValidateBoardScript(boardComp, out error);

        if (!ValidateBoardProgram(boardComp.Program, out error))
            return false;

        return true;
    }

    private void OnAgentInteractUsing(Entity<CMUUtilityDroneAgentComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<CMUUtilityDroneProgramBoardComponent>(args.Used))
            return;

        args.Handled = true;
        var user = args.User;
        var board = args.Used;
        if (!CanInstallProgramBoard(ent.Owner, board, user, out var error))
        {
            _popup.PopupEntity(error, ent, user, PopupType.SmallCaution);
            return;
        }

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            ent.Comp.BoardInstallDelay,
            new CMUUtilityDroneInstallBoardDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: board)
        {
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            BreakOnMove = true,
            NeedHand = true,
            DuplicateCondition = DuplicateConditions.SameEvent | DuplicateConditions.SameTarget | DuplicateConditions.SameTool,
            ExtraCheck = () => CanInstallProgramBoard(ent.Owner, board, user, out _),
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _popup.PopupEntity(
            Loc.GetString("cmu-utility-drone-popup-board-install-start"),
            ent,
            user);
    }

    private void OnInstallBoardComplete(Entity<CMUUtilityDroneAgentComponent> ent,
        ref CMUUtilityDroneInstallBoardDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var error = string.Empty;
        if (args.Cancelled ||
            args.Used is not { } board ||
            !CanInstallProgramBoard(ent.Owner, board, args.User, out error))
        {
            if (!args.Cancelled && !string.IsNullOrEmpty(error))
                _popup.PopupEntity(error, ent, args.User, PopupType.SmallCaution);

            return;
        }

        if (!TryInstallProgramBoard(ent.Owner, board, args.User, out error))
        {
            _popup.PopupEntity(error, ent, args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("cmu-utility-drone-popup-board-install-finish"),
            ent,
            args.User);
    }

    /// <summary>
    /// Inserts an already validated directive board into the drone, creates the
    /// board-install home anchor, records the installer and starts the program.
    /// </summary>
    public bool TryInstallProgramBoard(EntityUid drone, EntityUid board, EntityUid installer, out string error)
    {
        error = string.Empty;
        if (!CanInstallProgramBoard(drone, board, installer, out error) ||
            !TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent) ||
            !TryComp<CMUUtilityDroneProgramBoardComponent>(board, out var boardComp))
        {
            return false;
        }

        var container = EnsureProgramBoardContainer(drone, agent);
        if (!_containers.Insert(board, container))
        {
            error = Loc.GetString("cmu-utility-drone-error-board-install");
            return false;
        }

        agent.ProgramBoard = board;
        agent.BoardInstaller = installer;
        agent.FollowTarget = installer;
        SpawnHomeAnchor(drone, agent);

        var started = !string.IsNullOrWhiteSpace(boardComp.Script)
            ? StartScriptProgram(drone, agent, boardComp, out error)
            : StartProgram(drone, agent, boardComp.Program, out error);
        if (!started)
        {
            _containers.Remove(board, container, force: true);
            agent.ProgramBoard = null;
            agent.BoardInstaller = null;
            agent.FollowTarget = null;
            ClearHomeAnchor(agent);
            return false;
        }

        return true;
    }

    private bool StartProgram(
        EntityUid drone,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityProgram program,
        out string error)
    {
        if (!ValidateBoardProgram(program, out error))
            return false;

        if (program.Steps.Count == 0)
        {
            error = Loc.GetString("cmu-utility-drone-error-no-steps");
            return false;
        }

        var now = _timing.CurTime;
        _steering.Unregister(drone);
        SetScriptExecution(drone, null);
        agent.Program = program.Clone();
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
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-running");
        agent.NextThink = now;
        agent.NextSense = now;
        agent.NextPullCheck = now;
        EnsureNpcMovement(drone);
        EnsureComp<ActiveNPCComponent>(drone);
        return true;
    }

    /// <summary>
    /// Removes a board without deleting it. The caller owns dropping or handing
    /// the returned board to a player.
    /// </summary>
    public bool TryRemoveProgramBoard(EntityUid drone, out EntityUid board, out string error)
    {
        board = default;
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent))
        {
            error = Loc.GetString("cmu-utility-drone-error-agent");
            return false;
        }

        if (agent.BoardPortLocked)
        {
            error = Loc.GetString("cmu-utility-drone-error-board-port-locked");
            return false;
        }

        PruneProgramBoard(drone, agent);
        var container = EnsureProgramBoardContainer(drone, agent);
        if (container.ContainedEntity is not { } contained &&
            agent.ProgramBoard is not { } stored)
        {
            error = Loc.GetString("cmu-utility-drone-error-board-missing");
            return false;
        }

        board = container.ContainedEntity ?? agent.ProgramBoard!.Value;
        if (container.ContainedEntity is { } removed)
            _containers.Remove(removed, container, force: true);

        agent.ProgramBoard = null;
        agent.BoardInstaller = null;
        agent.FollowTarget = null;
        StopRuntime(drone, agent, CMUUtilityDroneStatus.Idle);
        agent.Program = new CMUUtilityProgram();
        ClearHomeAnchor(agent);
        return true;
    }

    public bool TryEjectProgramBoard(EntityUid drone, EntityUid user, out string error)
    {
        if (!TryRemoveProgramBoard(drone, out var board, out error))
            return false;

        if (user.IsValid() && !TerminatingOrDeleted(user))
            _hands.PickupOrDrop(user, board, dropNear: true);
        else
            _transform.SetCoordinates(board, Transform(drone).Coordinates);

        return true;
    }

    public bool PauseProgram(EntityUid drone, out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent) ||
            !agent.ProgramRunning ||
            agent.ProgramPaused)
        {
            error = Loc.GetString("cmu-utility-drone-error-not-running");
            return false;
        }

        agent.ProgramPaused = true;
        agent.Status = CMUUtilityDroneStatus.Paused;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-paused");
        _steering.Unregister(drone);
        return true;
    }

    public bool ResumeProgram(EntityUid drone, out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent) ||
            !agent.ProgramRunning ||
            !agent.ProgramPaused)
        {
            error = Loc.GetString("cmu-utility-drone-error-not-paused");
            return false;
        }

        if (!agent.ProgrammableMode)
        {
            error = Loc.GetString("cmu-utility-drone-error-programmable-mode");
            return false;
        }

        agent.ProgramPaused = false;
        agent.Status = CMUUtilityDroneStatus.Running;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-running");
        agent.NextThink = _timing.CurTime;
        return true;
    }

    public bool StopProgram(EntityUid drone, out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent) ||
            (!agent.ProgramRunning && !agent.ProgramPaused && !agent.ForcedReturn))
        {
            error = Loc.GetString("cmu-utility-drone-error-not-running");
            return false;
        }

        StopRuntime(drone, agent, CMUUtilityDroneStatus.Idle);
        return true;
    }

    /// <summary>
    /// Starts the installed board program again from step zero without
    /// requiring the board to be removed and reinstalled.
    /// </summary>
    public bool TryRestartProgram(EntityUid drone, out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent))
        {
            error = Loc.GetString("cmu-utility-drone-error-agent");
            return false;
        }

        if (!agent.ProgrammableMode)
        {
            error = Loc.GetString("cmu-utility-drone-error-programmable-mode");
            return false;
        }

        PruneProgramBoard(drone, agent);
        if (agent.ProgramBoard is not { } board ||
            TerminatingOrDeleted(board) ||
            !TryComp<CMUUtilityDroneProgramBoardComponent>(board, out var boardComp))
        {
            error = Loc.GetString("cmu-utility-drone-error-board-missing");
            return false;
        }

        if (agent.HomeAnchor is not { } home || TerminatingOrDeleted(home))
            SpawnHomeAnchor(drone, agent);

        var started = !string.IsNullOrWhiteSpace(boardComp.Script)
            ? StartScriptProgram(drone, agent, boardComp, out error)
            : StartProgram(drone, agent, boardComp.Program, out error);
        if (!started)
            return false;

        agent.NextThink = _timing.CurTime;
        return true;
    }

    public bool SameZNetwork(EntityUid first, EntityUid second)
    {
        return Transform(first).MapUid is { } firstMap &&
               Transform(second).MapUid is { } secondMap &&
               _zLevels.IsSameZNetwork(firstMap, secondMap);
    }

    private bool IsAlive(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) &&
               (!TryComp<MobStateComponent>(uid, out var mob) || _mobState.IsAlive(uid, mob));
    }

    private int CountProgrammableDrones()
    {
        var count = 0;
        var query = EntityQueryEnumerator<CMUUtilityDroneAgentComponent>();
        while (query.MoveNext(out _, out var agent))
        {
            if (agent.ProgrammableMode)
                count++;
        }

        return count;
    }

    private bool HasMultitoolInOtherHand(EntityUid user, EntityUid board)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held != board && HasComp<MultitoolComponent>(held))
                return true;
        }

        return false;
    }

    private ContainerSlot EnsureProgramBoardContainer(EntityUid drone, CMUUtilityDroneAgentComponent agent)
    {
        agent.ProgramBoardContainer ??=
            _containers.EnsureContainer<ContainerSlot>(drone, agent.ProgramBoardContainerId);
        agent.ProgramBoardContainer.OccludesLight = false;
        return agent.ProgramBoardContainer;
    }

    private void PruneProgramBoard(EntityUid drone, CMUUtilityDroneAgentComponent agent)
    {
        if (agent.ProgramBoard is not { } board ||
            TerminatingOrDeleted(board) ||
            !TryComp<CMUUtilityDroneProgramBoardComponent>(board, out _))
        {
            agent.ProgramBoard = null;
        }

        if (agent.ProgramBoard == null)
            return;

        if (agent.ProgramBoardContainer is { ContainedEntity: { } contained } &&
            contained != agent.ProgramBoard)
        {
            agent.ProgramBoard = contained;
        }
    }

    private void SpawnHomeAnchor(EntityUid drone, CMUUtilityDroneAgentComponent agent)
    {
        ClearHomeAnchor(agent);
        agent.HomeAnchor = Spawn(agent.HomeAnchorPrototype, Transform(drone).Coordinates);
    }

    private void ClearHomeAnchor(CMUUtilityDroneAgentComponent agent)
    {
        if (agent.HomeAnchor is { } anchor && !TerminatingOrDeleted(anchor))
            QueueDel(anchor);

        agent.HomeAnchor = null;
    }

    private CMUUtilityScriptExecution? GetScriptExecution(EntityUid uid)
    {
        return TryComp<CMUUtilityDroneRuntimeComponent>(uid, out var runtime)
            ? runtime.ScriptExecution
            : null;
    }

    private void SetScriptExecution(EntityUid uid, CMUUtilityScriptExecution? execution)
    {
        if (TryComp<CMUUtilityDroneRuntimeComponent>(uid, out var component))
            component.ScriptExecution = execution;
    }

    private bool HasScriptHandler(EntityUid uid, CMUUtilityTrigger trigger)
    {
        var execution = GetScriptExecution(uid);
        return execution != null && execution.Program.HasEventHandler(trigger);
    }

    private void StopRuntime(EntityUid uid, CMUUtilityDroneAgentComponent agent, CMUUtilityDroneStatus status)
    {
        agent.ProgramRunning = false;
        agent.ProgramPaused = false;
        agent.ForcedReturn = false;
        agent.StopAfterReturn = false;
        agent.PendingTrigger = null;
        agent.PendingSubject = null;
        agent.PendingTriggerAfter = null;
        agent.PendingSubjectAfter = null;
        agent.Destination = null;
        agent.WorkTarget = null;
        agent.FetchedItem = null;
        agent.LastPulledEntity = null;
        agent.LastAttacker = null;
        agent.DamagePending = false;
        agent.CombatTarget = null;
        agent.CombatCoordinates = null;
        agent.CombatWeapon = null;
        agent.CombatAbort = false;
        agent.CombatUntil = TimeSpan.Zero;
        SetScriptExecution(uid, null);
        agent.StepIndex = 0;
        agent.StepPhase = 0;
        agent.Waypoint = 0;
        agent.Status = status;
        agent.Activity = string.Empty;
        _steering.Unregister(uid);
    }
}
