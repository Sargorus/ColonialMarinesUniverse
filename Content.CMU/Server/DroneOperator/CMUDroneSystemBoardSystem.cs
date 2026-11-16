using System;
using System.Diagnostics.CodeAnalysis;
using Content.Server.CMU14.Robotics.UtilityDrones;
using Content.Shared._RMC14.Synth;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.DroneOperator;

/// <summary>
/// Installs the dedicated program and combat program system boards into drone
/// android slots. A program board turns a generic android into a programmable
/// utility drone without consuming the separate skills module slot.
/// </summary>
public sealed partial class CMUDroneSystemBoardSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private CMUDroneOperatorSystem _droneOperator = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSynthSystem _synth = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private CMUUtilityDroneSystem _utilityDrones = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUDroneAndroidComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<CMUDroneAndroidComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<CMUDroneAndroidComponent, CMUDroneSystemBoardInstallDoAfterEvent>(OnInstallComplete);
        SubscribeLocalEvent<CMUDroneAndroidComponent, CMUDroneSystemBoardRemoveDoAfterEvent>(OnRemoveComplete);
    }

    public bool CanUseCombatPrograms(EntityUid drone)
    {
        return TryComp<CMUDroneAndroidComponent>(drone, out var android) &&
               (android.StartsCombatProgramCapable || android.InstalledCombatSystem != null);
    }

    private void OnAfterInteractUsing(Entity<CMUDroneAndroidComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled ||
            !TryComp<CMUUtilityDroneSystemBoardComponent>(args.Used, out var board))
        {
            return;
        }

        args.Handled = true;

        if (!TryStartInstall(ent, (args.Used, board), args.User, out var error))
        {
            _popup.PopupEntity(error, ent, args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("cmu-drone-system-board-install-start", ("drone", ent.Owner)),
            ent,
            args.User);
    }

    private void OnInstallComplete(
        Entity<CMUDroneAndroidComponent> ent,
        ref CMUDroneSystemBoardInstallDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (args.Cancelled || args.Used is not { } board)
            return;

        if (!TryInstallSystemBoard(ent.Owner, board, args.User, out var error))
        {
            _popup.PopupEntity(error, ent, args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("cmu-drone-system-board-install-finish", ("drone", ent.Owner)),
            ent,
            args.User);
    }

    public bool HasProgramSystem(EntityUid drone)
    {
        return TryComp<CMUDroneAndroidComponent>(drone, out var android) &&
               android.InstalledProgramSystem != null;
    }

    public bool HasCombatSystem(EntityUid drone)
    {
        return TryComp<CMUDroneAndroidComponent>(drone, out var android) &&
               android.InstalledCombatSystem != null;
    }

    /// <summary>
    /// Installs a system board without waiting for a do-after. Hand, tool, and
    /// slot requirements are re-checked here.
    /// </summary>
    public bool TryInstallSystemBoard(
        EntityUid drone,
        EntityUid board,
        EntityUid user,
        out string error)
    {
        if (!CanInstallSystemBoard(drone, board, user, out error) ||
            !TryComp<CMUDroneAndroidComponent>(drone, out var android) ||
            !TryComp<CMUUtilityDroneSystemBoardComponent>(board, out var boardComp))
        {
            return false;
        }

        var container = EnsureBoardContainer(drone, android, boardComp.Combat);
        if (!_containers.Insert(board, container))
        {
            error = Loc.GetString("cmu-drone-system-board-install-failed");
            return false;
        }

        if (boardComp.Combat)
        {
            android.InstalledCombatSystem = board;
            SetCanUseGuns(drone, true);
        }
        else
        {
            android.InstalledProgramSystem = board;
        }

        _droneOperator.RefreshDroneSkills((drone, android));
        EnsureProgramAgent(drone);

        if (TryGetHeldTool(user, boardComp.InstallTool, board, out var tool, out var toolComp))
            _tool.PlayToolSound(tool, toolComp, user);

        return true;
    }

    public bool CanInstallSystemBoard(
        EntityUid drone,
        EntityUid board,
        EntityUid user,
        out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUDroneAndroidComponent>(drone, out var android) ||
            !TryComp<CMUUtilityDroneSystemBoardComponent>(board, out var boardComp))
        {
            error = Loc.GetString("cmu-drone-system-board-invalid");
            return false;
        }

        CacheBaseCanUseGuns(drone, android);

        if (!_mobState.IsAlive(drone))
        {
            error = Loc.GetString("cmu-drone-system-board-dead");
            return false;
        }

        var container = EnsureBoardContainer(drone, android, boardComp.Combat);
        if (container.ContainedEntity != null)
        {
            error = Loc.GetString(
                boardComp.Combat
                    ? "cmu-drone-system-board-combat-slot-full"
                    : "cmu-drone-system-board-slot-full");
            return false;
        }

        if (!_hands.IsHolding(user, board, out _))
        {
            error = Loc.GetString("cmu-drone-system-board-main-hand");
            return false;
        }

        if (!TryGetHeldTool(user, boardComp.InstallTool, board, out _, out _))
        {
            error = Loc.GetString(
                "cmu-drone-system-board-tool",
                ("tool", boardComp.InstallTool.Id));
            return false;
        }

        return true;
    }

    private bool TryStartInstall(
        Entity<CMUDroneAndroidComponent> drone,
        Entity<CMUUtilityDroneSystemBoardComponent> board,
        EntityUid user,
        out string error)
    {
        if (!CanInstallSystemBoard(drone.Owner, board.Owner, user, out error))
            return false;

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            board.Comp.InstallDelay,
            new CMUDroneSystemBoardInstallDoAfterEvent(),
            drone.Owner,
            target: drone.Owner,
            used: board.Owner)
        {
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            BreakOnMove = true,
            NeedHand = true,
            DuplicateCondition = DuplicateConditions.SameEvent | DuplicateConditions.SameTarget | DuplicateConditions.SameTool,
            ExtraCheck = () => CanInstallSystemBoard(drone.Owner, board.Owner, user, out _),
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            error = Loc.GetString("cmu-drone-system-board-install-failed");
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void OnGetVerbs(Entity<CMUDroneAndroidComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (ent.Comp.InstalledProgramSystem is { } program &&
            TryComp<CMUUtilityDroneSystemBoardComponent>(program, out var programComp) &&
            TryGetHeldTool(args.User, programComp.InstallTool, null, out var programTool, out _))
        {
            var user = args.User;
            var tool = programTool;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cmu-drone-system-board-remove-program-verb"),
                Priority = 2,
                Act = () => StartRemove(ent, false, user, tool),
            });
        }

        if (ent.Comp.InstalledCombatSystem is { } combat &&
            TryComp<CMUUtilityDroneSystemBoardComponent>(combat, out var combatComp) &&
            TryGetHeldTool(args.User, combatComp.InstallTool, null, out var combatTool, out _))
        {
            var user = args.User;
            var tool = combatTool;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cmu-drone-system-board-remove-combat-verb"),
                Priority = 2,
                Act = () => StartRemove(ent, true, user, tool),
            });
        }
    }

    private void StartRemove(
        Entity<CMUDroneAndroidComponent> drone,
        bool combat,
        EntityUid user,
        EntityUid tool)
    {
        if (!TryGetInstalledBoard(drone, combat, out _, out var boardComp))
            return;

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            boardComp.InstallDelay,
            new CMUDroneSystemBoardRemoveDoAfterEvent(combat),
            drone.Owner,
            target: drone.Owner,
            used: tool)
        {
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            BreakOnMove = true,
            NeedHand = true,
            DuplicateCondition = DuplicateConditions.SameEvent | DuplicateConditions.SameTarget | DuplicateConditions.SameTool,
            ExtraCheck = () => CanRemoveSystemBoard(drone.Owner, combat, user, tool, out _),
        };

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-drone-system-board-remove-start", ("drone", drone.Owner)),
                drone,
                user);
        }
    }

    private void OnRemoveComplete(
        Entity<CMUDroneAndroidComponent> ent,
        ref CMUDroneSystemBoardRemoveDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (args.Cancelled || args.Used is not { } tool)
            return;

        if (!TryRemoveSystemBoard(ent.Owner, args.Combat, args.User, tool, out var error))
        {
            if (!string.IsNullOrEmpty(error))
                _popup.PopupEntity(error, ent, args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("cmu-drone-system-board-remove-finish", ("drone", ent.Owner)),
            ent,
            args.User);
    }

    public bool TryRemoveSystemBoard(
        EntityUid drone,
        bool combat,
        EntityUid user,
        EntityUid tool,
        out string error)
    {
        if (!CanRemoveSystemBoard(drone, combat, user, tool, out error) ||
            !TryComp<CMUDroneAndroidComponent>(drone, out var android) ||
            !TryGetInstalledBoard((drone, android), combat, out var board, out var boardComp))
        {
            return false;
        }

        var container = EnsureBoardContainer(drone, android, combat);
        if (container.ContainedEntity == board)
            _containers.Remove(board, container, force: true);

        if (combat)
        {
            android.InstalledCombatSystem = null;
            if (!CanUseCombatPrograms(drone))
                StopCombatProgram(drone);
            SetCanUseGuns(drone, android.BaseCanUseGunsCached && android.BaseCanUseGuns);
        }
        else
        {
            android.InstalledProgramSystem = null;
        }

        _droneOperator.RefreshDroneSkills((drone, android));

        if (user.IsValid() && !TerminatingOrDeleted(user))
            _hands.PickupOrDrop(user, board, dropNear: true);
        else
            _transform.SetCoordinates(board, Transform(drone).Coordinates);
        if (TryComp<ToolComponent>(tool, out var toolComp))
            _tool.PlayToolSound(tool, toolComp, user);

        if (android.InstalledProgramSystem == null && android.InstalledCombatSystem == null)
        {
            if (TryComp<CMUUtilityDroneAgentComponent>(drone, out var agent))
            {
                agent.ProgrammableMode = false;
                _utilityDrones.TryEjectProgramBoard(drone, user, out _);
                RemComp<CMUUtilityDroneAgentComponent>(drone);
            }
        }

        return true;
    }

    private bool CanRemoveSystemBoard(
        EntityUid drone,
        bool combat,
        EntityUid user,
        EntityUid tool,
        out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUDroneAndroidComponent>(drone, out var android) ||
            !TryGetInstalledBoard((drone, android), combat, out _, out var boardComp))
        {
            error = Loc.GetString("cmu-drone-system-board-missing");
            return false;
        }

        CacheBaseCanUseGuns(drone, android);

        if (!_mobState.IsAlive(drone))
        {
            error = Loc.GetString("cmu-drone-system-board-dead");
            return false;
        }

        if (!TryComp<ToolComponent>(tool, out var toolComp) ||
            !_tool.HasQuality(tool, boardComp.InstallTool, toolComp))
        {
            error = Loc.GetString(
                "cmu-drone-system-board-tool",
                ("tool", boardComp.InstallTool.Id));
            return false;
        }

        return true;
    }

    private bool TryGetInstalledBoard(
        Entity<CMUDroneAndroidComponent> drone,
        bool combat,
        out EntityUid board,
        [NotNullWhen(true)] out CMUUtilityDroneSystemBoardComponent? boardComp)
    {
        var stored = combat ? drone.Comp.InstalledCombatSystem : drone.Comp.InstalledProgramSystem;
        board = stored ?? default;
        var container = EnsureBoardContainer(drone.Owner, drone.Comp, combat);
        if ((board == default || TerminatingOrDeleted(board)) && container.ContainedEntity is { } contained)
            board = contained;

        if (board != default &&
            !TerminatingOrDeleted(board) &&
            TryComp<CMUUtilityDroneSystemBoardComponent>(board, out boardComp))
        {
            if (combat)
                drone.Comp.InstalledCombatSystem = board;
            else
                drone.Comp.InstalledProgramSystem = board;

            return true;
        }

        boardComp = null;
        return false;
    }

    private void EnsureProgramAgent(EntityUid drone)
    {
        var agent = EnsureComp<CMUUtilityDroneAgentComponent>(drone);
        _utilityDrones.TrySetProgrammableMode(drone, true, out _);
        agent.BoardPortLocked = false;
    }

    private void StopCombatProgram(EntityUid drone)
    {
        if (TryComp<CMUUtilityDroneRuntimeComponent>(drone, out var runtime) &&
            runtime.ScriptExecution?.Program.UsesCombat == true)
        {
            _utilityDrones.StopProgram(drone, out _);
        }
    }

    private void CacheBaseCanUseGuns(EntityUid drone, CMUDroneAndroidComponent android)
    {
        if (android.BaseCanUseGunsCached ||
            !TryComp<SynthComponent>(drone, out var synth))
        {
            return;
        }

        android.BaseCanUseGuns = synth.CanUseGuns;
        android.BaseCanUseGunsCached = true;
    }

    private void SetCanUseGuns(EntityUid drone, bool canUseGuns)
    {
        _synth.SetGunRestriction(drone, canUseGuns);
    }

    private ContainerSlot EnsureBoardContainer(
        EntityUid drone,
        CMUDroneAndroidComponent android,
        bool combat)
    {
        var id = combat ? android.CombatSystemContainerId : android.ProgramSystemContainerId;
        var container = _containers.EnsureContainer<ContainerSlot>(drone, id);
        container.OccludesLight = false;
        return container;
    }

    private bool TryGetHeldTool(
        EntityUid user,
        ProtoId<ToolQualityPrototype> quality,
        EntityUid? except,
        out EntityUid tool,
        [NotNullWhen(true)] out ToolComponent? toolComp)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held == except || !TryComp<ToolComponent>(held, out toolComp))
                continue;

            if (!_tool.HasQuality(held, quality, toolComp))
                continue;

            tool = held;
            return true;
        }

        tool = default;
        toolComp = null;
        return false;
    }
}
