using Content.Shared._RMC14.Tools;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Console BUI, physical marker linking and directive-board printing. Every
/// program and marker mutation passes through server validation before it can
/// affect a drone.
/// </summary>
public sealed partial class CMUUtilityDroneConsoleSystem : EntitySystem
{
    private static readonly EntProtoId BoardPrototype = "CMUUtilityDroneProgramBoard";
    private static readonly TimeSpan MarkerLinkLifetime = TimeSpan.FromSeconds(30);

    [Dependency] private CMUUtilityDroneSystem _drones = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private readonly HashSet<EntityUid> _scanBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<CMUUtilityDroneConsoleComponent>(CMUUtilityDroneUiKey.Key, subs =>
        {
            subs.Event<CMUUtilityConsoleCommandMessage>(OnConsoleCommand);
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
        });

        SubscribeLocalEvent<CMUUtilityDroneConsoleComponent, InteractUsingEvent>(OnConsoleInteractUsing);
        SubscribeLocalEvent<CMUUtilityDroneConsoleComponent, ComponentShutdown>(OnConsoleShutdown);
        SubscribeLocalEvent<CMUUtilityTaskMarkerComponent, InteractUsingEvent>(OnMarkerInteractUsing);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CMUUtilityDroneConsoleComponent>();
        while (query.MoveNext(out var console, out var comp))
        {
            if (comp.NextUi > now || !_ui.IsUiOpen(console, CMUUtilityDroneUiKey.Key))
                continue;

            comp.NextUi = now + TimeSpan.FromSeconds(2);
            UpdateConsoleUi(console, comp);
        }
    }

    private void OnUiOpened(Entity<CMUUtilityDroneConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not CMUUtilityDroneUiKey)
            return;

        ent.Comp.NextUi = _timing.CurTime;
        UpdateConsoleUi(ent.Owner, ent.Comp);
    }

    private void OnConsoleShutdown(Entity<CMUUtilityDroneConsoleComponent> ent, ref ComponentShutdown args)
    {
        foreach (var marker in ent.Comp.Markers)
        {
            if (TerminatingOrDeleted(marker) ||
                !TryComp<CMUUtilityTaskMarkerComponent>(marker, out var markerData) ||
                markerData.Console != ent.Owner)
            {
                continue;
            }

            markerData.Console = null;
        }

        ent.Comp.Markers.Clear();
    }

    private void OnConsoleCommand(Entity<CMUUtilityDroneConsoleComponent> ent,
        ref CMUUtilityConsoleCommandMessage args)
    {
        HandleConsoleCommand(ent.Owner, args.Actor, args);
    }

    /// <summary>
    /// Public command entrypoint shared by the BUI and integration tests.
    /// A malformed message can never mutate a program, a marker or a board.
    /// </summary>
    public bool HandleConsoleCommand(EntityUid console, EntityUid actor, CMUUtilityConsoleCommandMessage args)
    {
        if (!TryComp<CMUUtilityDroneConsoleComponent>(console, out var comp))
            return false;

        if (!Enum.IsDefined(args.Command))
        {
            comp.RejectedCommands++;
            comp.ValidationError = Loc.GetString("cmu-utility-drone-error-malformed");
            UpdateIfOpen(console, comp);
            return false;
        }

        var now = _timing.CurTime;
        if (now < comp.NextCommand)
        {
            comp.RejectedCommands++;
            comp.Status = Loc.GetString("cmu-utility-drone-status-command-rate");
            UpdateIfOpen(console, comp);
            return false;
        }

        comp.NextCommand = now + TimeSpan.FromMilliseconds(100);

        switch (args.Command)
        {
            case CMUUtilityConsoleCommand.SaveProgram:
            case CMUUtilityConsoleCommand.PrintBoard:
                return HandleProgramCommand(console, actor, comp, args);

            case CMUUtilityConsoleCommand.UnlinkMarker:
                if (!TryGetEntity(args.Marker, out var marker) || marker is not { } markerUid)
                {
                    comp.ValidationError = Loc.GetString("cmu-utility-drone-error-marker");
                    UpdateIfOpen(console, comp);
                    return false;
                }

                if (!TryUnlinkMarker(console, markerUid, comp, out var unlinkError))
                {
                    comp.ValidationError = unlinkError;
                    UpdateIfOpen(console, comp);
                    return false;
                }

                if (comp.ScanMarker == markerUid)
                {
                    comp.ScanMarker = null;
                    comp.ScanItems.Clear();
                }

                comp.ValidationError = string.Empty;
                comp.Status = Loc.GetString("cmu-utility-drone-status-marker-unlinked");
                UpdateIfOpen(console, comp);
                return true;

            case CMUUtilityConsoleCommand.ScanItems:
                EntityUid? scanMarkerUid = null;
                if (args.Marker != NetEntity.Invalid)
                {
                    if (!TryGetEntity(args.Marker, out var scanMarker) || scanMarker is not { } resolvedScanMarker)
                    {
                        comp.ValidationError = Loc.GetString("cmu-utility-drone-error-marker");
                        UpdateIfOpen(console, comp);
                        return false;
                    }

                    scanMarkerUid = resolvedScanMarker;
                }

                if (!TryScanMarkerItems(console, scanMarkerUid, comp, out var scanError))
                {
                    comp.ValidationError = scanError;
                    UpdateIfOpen(console, comp);
                    return false;
                }

                comp.ValidationError = string.Empty;
                comp.Status = Loc.GetString("cmu-utility-drone-status-items-scanned",
                    ("count", comp.ScanItems.Count));
                UpdateIfOpen(console, comp);
                return true;

            default:
                comp.RejectedCommands++;
                comp.ValidationError = Loc.GetString("cmu-utility-drone-error-malformed");
                UpdateIfOpen(console, comp);
                return false;
        }
    }

    private bool HandleProgramCommand(
        EntityUid console,
        EntityUid actor,
        CMUUtilityDroneConsoleComponent comp,
        CMUUtilityConsoleCommandMessage args)
    {
        var now = _timing.CurTime;
        if (now < comp.NextSave)
        {
            comp.RejectedSaves++;
            comp.Status = Loc.GetString("cmu-utility-drone-status-save-rate");
            UpdateIfOpen(console, comp);
            return false;
        }

        comp.NextSave = now + TimeSpan.FromSeconds(1);
        if (args.Program == null)
        {
            comp.RejectedSaves++;
            comp.ValidationError = Loc.GetString("cmu-utility-drone-error-no-program");
            comp.Status = Loc.GetString("cmu-utility-drone-status-rejected");
            UpdateIfOpen(console, comp);
            return false;
        }

        string error;
        if (!string.IsNullOrWhiteSpace(args.Program.Script))
        {
            if (!_drones.ValidateScript(
                    console,
                    args.Program.Script,
                    out _,
                    out _,
                    out error))
            {
                comp.RejectedSaves++;
                comp.ValidationError = error;
                comp.Status = Loc.GetString("cmu-utility-drone-status-rejected");
                UpdateIfOpen(console, comp);
                return false;
            }

            comp.Script = args.Program.Script;
            comp.Program = new CMUUtilityProgram();
        }
        else
        {
            if (!_drones.ValidateProgram(console, args.Program, out var stepProgram, out error))
            {
                comp.RejectedSaves++;
                comp.ValidationError = error;
                comp.Status = Loc.GetString("cmu-utility-drone-status-rejected");
                UpdateIfOpen(console, comp);
                return false;
            }

            comp.Script = string.Empty;
            comp.Program = stepProgram.Clone();
        }

        comp.ValidationError = string.Empty;

        if (args.Command == CMUUtilityConsoleCommand.SaveProgram)
        {
            comp.Status = Loc.GetString("cmu-utility-drone-status-saved");
            UpdateIfOpen(console, comp);
            return true;
        }

        if (!TryPrintProgramBoard(console, actor, args.Program, out _, out error, comp))
        {
            comp.ValidationError = error;
            comp.Status = Loc.GetString("cmu-utility-drone-status-rejected");
            UpdateIfOpen(console, comp);
            return false;
        }

        comp.Status = Loc.GetString("cmu-utility-drone-status-board-printed");
        UpdateIfOpen(console, comp);
        return true;
    }

    /// <summary>
    /// Validates a program and prints one physical directive board. The board
    /// stays in the world until it is installed into a programmable drone.
    /// </summary>
    public bool TryPrintProgramBoard(
        EntityUid console,
        EntityUid actor,
        CMUUtilityProgramInfo? info,
        out EntityUid board,
        out string error,
        CMUUtilityDroneConsoleComponent? comp = null)
    {
        board = default;
        error = string.Empty;

        if (!TryComp(console, out comp))
        {
            error = Loc.GetString("cmu-utility-drone-error-controller");
            return false;
        }

        CMUUtilityProgram program;
        string script;
        List<EntityUid> scriptMarkers;
        if (info != null && !string.IsNullOrWhiteSpace(info.Script))
        {
            if (!_drones.ValidateScript(
                    console,
                    info.Script,
                    out _,
                    out scriptMarkers,
                    out error))
            {
                return false;
            }

            program = new CMUUtilityProgram();
            script = info.Script;
        }
        else
        {
            if (!_drones.ValidateProgram(console, info, out program, out error))
                return false;

            script = string.Empty;
            scriptMarkers = new List<EntityUid>();
        }

        var spawnCoordinates = actor.IsValid() && !TerminatingOrDeleted(actor)
            ? Transform(actor).Coordinates
            : Transform(console).Coordinates;
        board = Spawn(BoardPrototype, spawnCoordinates);
        var boardComp = EnsureComp<CMUUtilityDroneProgramBoardComponent>(board);
        boardComp.Program = program.Clone();
        boardComp.Script = script;
        boardComp.ScriptMarkers = scriptMarkers;
        boardComp.Console = console;
        comp.Program = program.Clone();
        comp.Script = script;

        if (actor.IsValid() &&
            !TerminatingOrDeleted(actor) &&
            !_hands.TryPickupAnyHand(actor, board))
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-utility-drone-popup-board-printed-floor"),
                actor,
                actor,
                PopupType.SmallCaution);
        }

        return true;
    }

    /// <summary>
    /// Registers a physical, anchored marker with one console. A marker can
    /// belong to only one console at a time.
    /// </summary>
    public bool TryLinkMarker(EntityUid console, EntityUid marker, EntityUid actor, out string error)
    {
        error = string.Empty;

        if (!TryComp<CMUUtilityDroneConsoleComponent>(console, out var comp))
        {
            error = Loc.GetString("cmu-utility-drone-error-controller");
            return false;
        }

        if (TerminatingOrDeleted(marker) ||
            !TryComp<CMUUtilityTaskMarkerComponent>(marker, out var markerData))
        {
            error = Loc.GetString("cmu-utility-drone-error-marker");
            return false;
        }

        if (!Transform(marker).Anchored)
        {
            error = Loc.GetString("cmu-utility-drone-error-marker-unanchored");
            return false;
        }

        if (!_drones.SameZNetwork(console, marker))
        {
            error = Loc.GetString("cmu-utility-drone-error-z-network");
            return false;
        }

        if (markerData.Console is { } linked && linked != console)
        {
            error = Loc.GetString("cmu-utility-drone-error-marker-linked-other");
            return false;
        }

        PruneMarkers(comp);
        if (comp.Markers.Contains(marker))
        {
            error = Loc.GetString("cmu-utility-drone-error-marker-already-linked");
            return false;
        }

        if (comp.Markers.Count >= CMUUtilityDroneLimits.MaxMarkers)
        {
            error = Loc.GetString("cmu-utility-drone-error-marker-limit",
                ("limit", CMUUtilityDroneLimits.MaxMarkers));
            return false;
        }

        markerData.Console = console;
        comp.Markers.Add(marker);
        comp.ValidationError = string.Empty;
        comp.Status = Loc.GetString("cmu-utility-drone-status-marker-linked");
        UpdateIfOpen(console, comp);
        return true;
    }

    private bool TryUnlinkMarker(
        EntityUid console,
        EntityUid marker,
        CMUUtilityDroneConsoleComponent comp,
        out string error)
    {
        error = string.Empty;
        if (!TryComp<CMUUtilityTaskMarkerComponent>(marker, out var markerData) ||
            markerData.Console != console ||
            !comp.Markers.Remove(marker))
        {
            error = Loc.GetString("cmu-utility-drone-error-marker");
            return false;
        }

        markerData.Console = null;
        return true;
    }

    private bool TryScanMarkerItems(
        EntityUid console,
        EntityUid? marker,
        CMUUtilityDroneConsoleComponent comp,
        out string error)
    {
        error = string.Empty;
        var origin = Transform(console).Coordinates;
        if (marker is { } markerUid)
        {
            if (!comp.Markers.Contains(markerUid) ||
                TerminatingOrDeleted(markerUid) ||
                !TryComp<CMUUtilityTaskMarkerComponent>(markerUid, out var markerData) ||
                markerData.Console != console ||
                !Transform(markerUid).Anchored ||
                !_drones.SameZNetwork(console, markerUid))
            {
                error = Loc.GetString("cmu-utility-drone-error-marker");
                comp.ScanMarker = null;
                comp.ScanItems.Clear();
                return false;
            }

            origin = Transform(markerUid).Coordinates;
        }

        comp.ScanMarker = marker;
        comp.ScanItems.Clear();
        _scanBuffer.Clear();
        _lookup.GetEntitiesInRange(
            origin,
            8f,
            _scanBuffer,
            LookupFlags.Uncontained);

        var candidates = 0;
        foreach (var candidate in _scanBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;

            if (TerminatingOrDeleted(candidate) ||
                (marker is { } scanMarkerUid && candidate == scanMarkerUid) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                HasComp<CMUUtilityDroneConsoleComponent>(candidate) ||
                Transform(candidate).Anchored)
            {
                continue;
            }

            var isItem = HasComp<ItemComponent>(candidate);
            var isPullable = TryComp<PullableComponent>(candidate, out _) &&
                             TryComp<PhysicsComponent>(candidate, out var physics) &&
                             physics.BodyType != BodyType.Static;
            if (!isItem && !isPullable)
                continue;

            comp.ScanItems.Add(candidate);
        }

        return true;
    }

    private bool TryDescribeItem(EntityUid uid, out CMUUtilityItemInfo info)
    {
        info = default!;
        if (TerminatingOrDeleted(uid) ||
            Transform(uid).Anchored ||
            HasComp<CMUUtilityTaskMarkerComponent>(uid) ||
            HasComp<CMUUtilityDroneAgentComponent>(uid))
        {
            return false;
        }

        var isItem = HasComp<ItemComponent>(uid);
        var isPullable = TryComp<PullableComponent>(uid, out _) &&
                         TryComp<PhysicsComponent>(uid, out var physics) &&
                         physics.BodyType != BodyType.Static;
        if (!isItem && !isPullable)
            return false;

        var label = MetaData(uid).EntityName;
        if (string.IsNullOrWhiteSpace(label))
            label = uid.ToString();

        info = new CMUUtilityItemInfo(GetNetEntity(uid), label, isItem, isPullable);
        return true;
    }

    private void OnMarkerInteractUsing(Entity<CMUUtilityTaskMarkerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<MultitoolComponent>(args.Used))
            return;

        args.Handled = true;
        if (!Transform(ent).Anchored)
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-utility-drone-popup-marker-anchor-first"),
                ent,
                args.User,
                PopupType.SmallCaution);
            return;
        }

        var linker = EnsureComp<CMUUtilityMarkerLinkerComponent>(args.Used);
        linker.PendingMarker = ent.Owner;
        linker.Expires = _timing.CurTime + MarkerLinkLifetime;
        _popup.PopupEntity(
            Loc.GetString("cmu-utility-drone-popup-marker-selected"),
            ent,
            args.User);
    }

    private void OnConsoleInteractUsing(Entity<CMUUtilityDroneConsoleComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<MultitoolComponent>(args.Used))
            return;

        args.Handled = true;
        if (!TryComp<CMUUtilityMarkerLinkerComponent>(args.Used, out var linker) ||
            linker.PendingMarker is not { } marker ||
            TerminatingOrDeleted(marker) ||
            linker.Expires < _timing.CurTime)
        {
            if (linker != null)
                linker.PendingMarker = null;

            _popup.PopupEntity(
                Loc.GetString("cmu-utility-drone-popup-marker-select-first"),
                ent,
                args.User,
                PopupType.SmallCaution);
            return;
        }

        linker.PendingMarker = null;
        if (!TryLinkMarker(ent.Owner, marker, args.User, out var error))
        {
            _popup.PopupEntity(error, ent, args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("cmu-utility-drone-popup-marker-linked"),
            ent,
            args.User);
    }

    public void UpdateConsoleUi(EntityUid console, CMUUtilityDroneConsoleComponent? comp = null)
    {
        if (!TryComp(console, out comp))
            return;

        PruneMarkers(comp);
        var markers = new List<CMUUtilityMarkerInfo>(comp.Markers.Count);
        foreach (var marker in comp.Markers)
        {
            if (TerminatingOrDeleted(marker) ||
                !TryComp<CMUUtilityTaskMarkerComponent>(marker, out var markerData) ||
                markerData.Console != console)
            {
                continue;
            }

            markers.Add(new CMUUtilityMarkerInfo(
                GetNetEntity(marker),
                Transform(marker).Anchored));
        }

        markers.Sort((a, b) => a.Marker.Id.CompareTo(b.Marker.Id));

        if (comp.ScanMarker is { } scanMarker &&
            (!comp.Markers.Contains(scanMarker) || TerminatingOrDeleted(scanMarker)))
        {
            comp.ScanMarker = null;
            comp.ScanItems.Clear();
        }

        var nearbyItems = new List<CMUUtilityItemInfo>(comp.ScanItems.Count);
        foreach (var item in comp.ScanItems)
        {
            if (TryDescribeItem(item, out var info))
                nearbyItems.Add(info);
        }

        nearbyItems.Sort((a, b) =>
            string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));

        _ui.SetUiState(
            console,
            CMUUtilityDroneUiKey.Key,
            new CMUUtilityDroneConsoleState(
                markers,
                _drones.ToProgramInfo(comp.Program),
                comp.Status,
                comp.ValidationError,
                nearbyItems,
                comp.ScanMarker is { } currentScan ? GetNetEntity(currentScan) : NetEntity.Invalid,
                comp.Script));
    }

    private void PruneMarkers(CMUUtilityDroneConsoleComponent comp)
    {
        for (var i = comp.Markers.Count - 1; i >= 0; i--)
        {
            var marker = comp.Markers[i];
            if (TerminatingOrDeleted(marker) ||
                !TryComp<CMUUtilityTaskMarkerComponent>(marker, out var markerData) ||
                markerData.Console != comp.Owner)
            {
                comp.Markers.RemoveAt(i);
                if (!TerminatingOrDeleted(marker) &&
                    TryComp<CMUUtilityTaskMarkerComponent>(marker, out var existing) &&
                    existing.Console == comp.Owner)
                {
                    existing.Console = null;
                }
            }
        }
    }

    private void UpdateIfOpen(EntityUid console, CMUUtilityDroneConsoleComponent comp)
    {
        comp.NextUi = _timing.CurTime + TimeSpan.FromSeconds(2);
        if (_ui.IsUiOpen(console, CMUUtilityDroneUiKey.Key))
            UpdateConsoleUi(console, comp);
    }
}

[RegisterComponent]
public sealed partial class CMUUtilityMarkerLinkerComponent : Component
{
    public EntityUid? PendingMarker;
    public TimeSpan Expires;
}
