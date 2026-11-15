using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.CMU14.Ops.ThirdParty;
using Content.Server._RMC14.Requisitions;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Threats;
using Content.Shared.CMU14.Threats.Mobs.Wendigo.Lab;
using Content.Shared.CMU14.Yautja;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.HiveLeader;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleSystem
{
    private const string CorporateFaction = "corporate";
    private const string PmcPartyId = "CMUWYPMCTeamSmall";
    private const string GuardPartyId = "CMUWYSecurityTeam";
    private static readonly EntProtoId YautjaTechnologyCrate = "CMUCrateSecureYautjaTechnology";
    private const int MaxActiveMissions = 3;
    private const int MaxScanMissionsPerRound = 3;
    private const int PointsMissionStep = 10;

    private static readonly ProtoId<ThirdPartyPrototype> PmcParty = new(PmcPartyId);
    private static readonly ProtoId<ThirdPartyPrototype> GuardParty = new(GuardPartyId);

    private enum CompanyMissionKind : byte
    {
        RoboticsPoints,
        ChemistryPoints,
        ChemistryScanSimple,
        ChemistryScanComplex,
        CreateWendigo,
        CorruptedHiveRepresentative,
        CorruptedHiveAlliance,
        PredatorBracer,
    }

    private sealed record CompanyMissionDefinition(
        string Id,
        CompanyMissionKind Kind,
        string NameLoc,
        string DescriptionLoc,
        string ObjectiveLoc,
        int Reward,
        int Target,
        int TimeLimitMinutes,
        bool Repeatable = false);

    private sealed class ActiveCompanyMission
    {
        public CompanyMissionDefinition Definition = default!;
        public TimeSpan Deadline;
        public int Progress;
        public int Target;
        public int Reward;
    }

    private sealed class MissionRecord
    {
        public CompanyMissionDefinition Definition = default!;
        public CompanyMissionStatus Status;
        public int Progress;
        public int Target;
        public int Reward;
        public int SecondsRemaining;
    }

    private sealed class FactionMissionState
    {
        public CorporateResearchDirection Direction;

        /// <summary>The liaison's third currency: how valuable the complex is to the company.</summary>
        public int CompanyValue;

        /// <summary>Scan missions started this round, across both simple and complex varieties.</summary>
        public int ScanMissionsStarted;

        /// <summary>How many times each escalating points mission has been completed.</summary>
        public readonly Dictionary<string, int> PointsMissionCompletions =
            new(StringComparer.OrdinalIgnoreCase);

        public readonly List<ActiveCompanyMission> Active = new();
        public readonly List<MissionRecord> History = new();
        public readonly HashSet<string> CompletedIds = new(StringComparer.Ordinal);
        public readonly HashSet<string> FailedIds = new(StringComparer.Ordinal);
        public string Status = string.Empty;
    }

    /// <summary>
    /// The company mission board. Values are intentionally in one place so balance passes do not have to hunt
    /// through UI code: targets are absolute balances, time limits run from acceptance, and every mission pays
    /// company value. The Yautja bracer task is repeatable so every recovered bracer can be rewarded.
    /// </summary>
    private static readonly CompanyMissionDefinition[] MissionCatalog =
    [
        new(
            "robotics-points",
            CompanyMissionKind.RoboticsPoints,
            "cmu-company-mission-robotics-name",
            "cmu-company-mission-robotics-desc",
            "cmu-company-mission-robotics-objective",
            Reward: 5,
            Target: 25,
            TimeLimitMinutes: 30,
            Repeatable: true),
        new(
            "chemistry-points",
            CompanyMissionKind.ChemistryPoints,
            "cmu-company-mission-chemistry-name",
            "cmu-company-mission-chemistry-desc",
            "cmu-company-mission-chemistry-objective",
            Reward: 5,
            Target: 20,
            TimeLimitMinutes: 30,
            Repeatable: true),
        new(
            "chemistry-scan-simple",
            CompanyMissionKind.ChemistryScanSimple,
            "cmu-company-mission-scan-simple-name",
            "cmu-company-mission-scan-simple-desc",
            "cmu-company-mission-scan-objective",
            Reward: 10,
            Target: 1,
            TimeLimitMinutes: 45,
            Repeatable: true),
        new(
            "chemistry-scan-complex",
            CompanyMissionKind.ChemistryScanComplex,
            "cmu-company-mission-scan-complex-name",
            "cmu-company-mission-scan-complex-desc",
            "cmu-company-mission-scan-objective",
            Reward: 10,
            Target: 1,
            TimeLimitMinutes: 75,
            Repeatable: true),
        new(
            "create-wendigo",
            CompanyMissionKind.CreateWendigo,
            "cmu-company-mission-wendigo-name",
            "cmu-company-mission-wendigo-desc",
            "cmu-company-mission-wendigo-objective",
            Reward: 50,
            Target: 1,
            TimeLimitMinutes: 90),
        new(
            "corrupted-hive-representative",
            CompanyMissionKind.CorruptedHiveRepresentative,
            "cmu-company-mission-hive-rep-name",
            "cmu-company-mission-hive-rep-desc",
            "cmu-company-mission-single-objective",
            Reward: 60,
            Target: 1,
            TimeLimitMinutes: 90),
        new(
            "corrupted-hive-alliance",
            CompanyMissionKind.CorruptedHiveAlliance,
            "cmu-company-mission-hive-alliance-name",
            "cmu-company-mission-hive-alliance-desc",
            "cmu-company-mission-single-objective",
            Reward: 20,
            Target: 1,
            TimeLimitMinutes: 90),
        new(
            "predator-bracer",
            CompanyMissionKind.PredatorBracer,
            "cmu-company-mission-bracer-name",
            "cmu-company-mission-bracer-desc",
            "cmu-company-mission-bracer-objective",
            Reward: 1,
            Target: 1,
            TimeLimitMinutes: 60,
            Repeatable: true),
    ];

    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedXenoHiveSystem _xenoHive = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private RequisitionsSystem _requisitions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<string, FactionMissionState> _missionStates =
        new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan _nextMissionTick;
    private TimeSpan _nextUiRefresh;

    private FactionMissionState GetMissionState(string faction)
    {
        if (_missionStates.TryGetValue(faction, out var state))
            return state;

        state = new FactionMissionState();
        _missionStates[faction] = state;
        return state;
    }

    private CMUScienceStatusQueryEvent QueryScience(string faction)
    {
        var ev = new CMUScienceStatusQueryEvent(faction);
        RaiseLocalEvent(ev);
        return ev;
    }

    private void ResetMissionState()
    {
        _missionStates.Clear();
        _nextMissionTick = default;
    }

    public CorporateResearchDirection GetResearchDirection(string faction)
    {
        return GetMissionState(faction).Direction;
    }

    public int GetCompanyValue(string faction)
    {
        return GetMissionState(faction).CompanyValue;
    }

    /// <summary>Sets the liaison's company value. Used by admin tools and tests.</summary>
    public void SetCompanyValue(string faction, int value)
    {
        GetMissionState(faction).CompanyValue = Math.Max(0, value);
        UpdateAllUi();
    }

    private static bool IsPointsMission(CompanyMissionKind kind)
    {
        return kind is CompanyMissionKind.RoboticsPoints or CompanyMissionKind.ChemistryPoints;
    }

    private static bool IsScanMission(CompanyMissionKind kind)
    {
        return kind is CompanyMissionKind.ChemistryScanSimple or CompanyMissionKind.ChemistryScanComplex;
    }

    private static int GetPointsMissionTarget(FactionMissionState state, CompanyMissionDefinition definition)
    {
        if (!IsPointsMission(definition.Kind))
            return definition.Target;

        var completed = state.PointsMissionCompletions.GetValueOrDefault(definition.Id);
        return definition.Target + completed * PointsMissionStep;
    }

    private void OnSetDirection(EntityUid uid, CorporateConsoleComponent comp,
        CorporateConsoleSetDirectionBuiMsg msg)
    {
        if (msg.Direction is not (CorporateResearchDirection.Robotics or CorporateResearchDirection.Bioengineering))
            return;

        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (state.Direction == msg.Direction)
            return;

        state.Direction = msg.Direction;
        state.Status = Loc.GetString(
            "cmu-company-direction-set",
            ("direction", DirectionName(msg.Direction)));
        UpdateAllUi();
    }

    private void OnAcceptMission(EntityUid uid, CorporateConsoleComponent comp,
        CorporateConsoleAcceptMissionBuiMsg msg)
    {
        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        var definition = MissionCatalog.FirstOrDefault(candidate => candidate.Id == msg.MissionId);
        if (definition == null)
            return;

        if (!definition.Repeatable &&
            (state.CompletedIds.Contains(definition.Id) || state.FailedIds.Contains(definition.Id)))
        {
            state.Status = Loc.GetString("cmu-company-mission-already-done");
            UpdateAllUi();
            return;
        }

        if (state.Active.Any(mission => mission.Definition.Id == definition.Id))
        {
            state.Status = Loc.GetString("cmu-company-mission-already-active");
            UpdateAllUi();
            return;
        }

        if (state.Active.Count >= MaxActiveMissions)
        {
            state.Status = Loc.GetString("cmu-company-mission-limit", ("limit", MaxActiveMissions));
            UpdateAllUi();
            return;
        }

        if (IsScanMission(definition.Kind) && state.ScanMissionsStarted >= MaxScanMissionsPerRound)
        {
            state.Status = Loc.GetString(
                "cmu-company-mission-scan-limit",
                ("limit", MaxScanMissionsPerRound));
            UpdateAllUi();
            return;
        }

        var target = GetPointsMissionTarget(state, definition);
        var active = new ActiveCompanyMission
        {
            Definition = definition,
            Deadline = _timing.CurTime + TimeSpan.FromMinutes(definition.TimeLimitMinutes),
            Target = target,
            Reward = definition.Reward,
        };

        if (IsPointsMission(definition.Kind))
        {
            var science = QueryScience(faction);
            var current = definition.Kind == CompanyMissionKind.RoboticsPoints
                ? science.RoboticsPoints
                : science.ChemistryPoints;
            active.Progress = Math.Min(current, active.Target);
        }

        if (IsScanMission(definition.Kind))
            state.ScanMissionsStarted++;

        state.Active.Add(active);
        state.Status = Loc.GetString(
            "cmu-company-mission-accepted",
            ("name", Loc.GetString(definition.NameLoc)));
        UpdateAllUi();
    }

    private void OnCallPmc(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleCallPmcBuiMsg msg)
    {
        if (comp.PmcCalls >= comp.MaxPmcCalls || _timing.CurTime < comp.NextPmcCallAt)
            return;

        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (state.CompanyValue < comp.PmcCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-pmc-no-value");
            UpdateAllUi();
            return;
        }

        if (!_proto.TryIndex<ThirdPartyPrototype>(PmcParty, out var partyProto)
            || !_auRound.IsThirdPartyAllowedForCurrentContext(partyProto)
            || !_proto.TryIndex(partyProto.PartySpawn, out var spawnProto))
            return;

        _thirdParty.SpawnThirdParty(partyProto, spawnProto, false,
            caller: new ThirdPartyCaller(
                Name(msg.Actor),
                Loc.GetString("cmu-third-party-caller-corporate"),
                Actor: msg.Actor,
                Console: uid));

        var nextCall = _timing.CurTime + comp.PmcCooldown;
        var query = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var other))
        {
            other.PmcCalls++;
            other.NextPmcCallAt = nextCall;
            SetSupportStatus(other, "cmu-corporate-pmc-called");
            _ui.SetUiState(consoleUid, CorporateConsoleUi.Key, BuildUiState(consoleUid, other));
        }
    }

    private void OnCallGuard(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleCallGuardBuiMsg msg)
    {
        if (comp.GuardCalls >= comp.MaxGuardCalls || _timing.CurTime < comp.NextGuardCallAt)
            return;

        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (state.CompanyValue < comp.GuardCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-guard-no-value");
            UpdateAllUi();
            return;
        }

        if (!_proto.TryIndex<ThirdPartyPrototype>(GuardParty, out var partyProto)
            || !_auRound.IsThirdPartyAllowedForCurrentContext(partyProto)
            || !_proto.TryIndex(partyProto.PartySpawn, out var spawnProto))
            return;

        _thirdParty.SpawnThirdParty(partyProto, spawnProto, false,
            caller: new ThirdPartyCaller(
                Name(msg.Actor),
                Loc.GetString("cmu-third-party-caller-corporate"),
                Actor: msg.Actor,
                Console: uid));

        state.CompanyValue -= comp.GuardCostCompanyValue;

        var nextCall = _timing.CurTime + comp.GuardCooldown;
        var query = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var other))
        {
            other.GuardCalls++;
            other.NextGuardCallAt = nextCall;
            SetSupportStatus(other, "cmu-corporate-guard-called");
            _ui.SetUiState(consoleUid, CorporateConsoleUi.Key, BuildUiState(consoleUid, other));
        }
    }

    private void OnRequestCredit(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleRequestCreditBuiMsg msg)
    {
        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (_timing.CurTime < comp.NextCreditAt)
            return;

        if (state.CompanyValue < comp.CreditCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-credit-no-value");
            UpdateAllUi();
            return;
        }

        state.CompanyValue -= comp.CreditCostCompanyValue;
        comp.NextCreditAt = _timing.CurTime + comp.CreditCooldown;
        AddToCorporateBudget(comp.CreditAmount);
        SetSupportStatus(comp, "cmu-corporate-credit-granted");
        UpdateAllUi();
    }

    private void OnRequestAlienTech(EntityUid uid, CorporateConsoleComponent comp,
        CorporateConsoleRequestAlienTechBuiMsg msg)
    {
        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (state.CompanyValue < comp.AlienTechCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-alien-tech-no-value");
            UpdateAllUi();
            return;
        }

        if (FindCorporateElevator(uid) is not { } elevator)
        {
            SetSupportStatus(comp, "cmu-corporate-alien-tech-no-cargo");
            UpdateAllUi();
            return;
        }

        var entry = new RequisitionsEntry
        {
            Crate = YautjaTechnologyCrate,
        };

        if (!_requisitions.TryAddElevatorOrders(elevator, new List<RequisitionsEntry> { entry }))
        {
            SetSupportStatus(comp, "cmu-corporate-alien-tech-queue-full");
            UpdateAllUi();
            return;
        }

        state.CompanyValue -= comp.AlienTechCostCompanyValue;
        SetSupportStatus(comp, "cmu-corporate-alien-tech-queued");
        UpdateAllUi();
    }

    private void SetSupportStatus(CorporateConsoleComponent comp, string locId)
    {
        comp.SupportStatus = Loc.GetString(locId);
    }

    private Entity<RequisitionsElevatorComponent>? FindCorporateElevator(EntityUid console)
    {
        var map = _transform.GetMapId(console);
        var origin = _transform.GetWorldPosition(console);
        Entity<RequisitionsElevatorComponent>? closest = null;
        var closestDistance = float.MaxValue;

        var query = EntityQueryEnumerator<RequisitionsElevatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var elevator, out var xform))
        {
            if (xform.MapID != map ||
                !string.Equals(elevator.Faction, "corporate", StringComparison.OrdinalIgnoreCase))
                continue;

            var distance = (origin - _transform.GetWorldPosition(uid)).LengthSquared();
            if (distance >= closestDistance)
                continue;

            closest = (uid, elevator);
            closestDistance = distance;
        }

        return closest;
    }

    private void OnChemicalScanned(CMUChemicalScannedEvent args)
    {
        if (args.Properties.Count == 0)
            return;

        var state = GetMissionState(args.Faction);
        var changed = false;

        for (var i = state.Active.Count - 1; i >= 0; i--)
        {
            var mission = state.Active[i];
            var matches = mission.Definition.Kind switch
            {
                CompanyMissionKind.ChemistryScanSimple => IsSimplePropertyScan(args.Properties),
                CompanyMissionKind.ChemistryScanComplex => IsComplexPropertyScan(args.Properties),
                _ => false,
            };

            if (!matches)
                continue;

            if (_timing.CurTime >= mission.Deadline)
                continue;

            mission.Progress = 1;
            CompleteMission(args.Faction, state, i);
            changed = true;
        }

        if (changed)
            UpdateAllUi();
    }

    private bool IsSimplePropertyScan(Dictionary<string, int> properties)
    {
        if (properties.Count < 2)
            return false;

        var hasPositive = false;
        var hasNegative = false;
        foreach (var propertyId in properties.Keys)
        {
            if (!_proto.TryIndex<ReagentPropertyPrototype>(propertyId, out var property))
                return false;

            if (property.Rarity >= ReagentPropertyRarityEnum.Rare)
                return false;

            if (property.Hint == ReagentPropertyHintEnum.Positive)
                hasPositive = true;
            else if (property.Hint == ReagentPropertyHintEnum.Negative)
                hasNegative = true;
        }

        // The property system has no explicit antipode field. A positive/negative pair is the closest
        // analogue and is exactly what the synthesis machine can transfer today.
        return hasPositive && hasNegative;
    }

    private bool IsComplexPropertyScan(Dictionary<string, int> properties)
    {
        foreach (var propertyId in properties.Keys)
        {
            if (!_proto.TryIndex<ReagentPropertyPrototype>(propertyId, out var property))
                return false;

            if (property.Rarity >= ReagentPropertyRarityEnum.Rare)
                return true;
        }

        return false;
    }

    private void OnWendigoCreated(CMUWendigoCreatedEvent args)
    {
        var changed = false;
        foreach (var (faction, state) in _missionStates.ToArray())
        {
            for (var i = state.Active.Count - 1; i >= 0; i--)
            {
                var mission = state.Active[i];
                if (mission.Definition.Kind != CompanyMissionKind.CreateWendigo)
                    continue;

                if (_timing.CurTime >= mission.Deadline)
                    continue;

                var reward = mission.Definition.Reward;
                if (!args.FullWendigo)
                    reward = Math.Max(1, reward / 2);
                if (args.Tamed)
                    reward *= 2;

                mission.Progress = 1;
                mission.Reward = reward;
                CompleteMission(faction, state, i);
                changed = true;
            }
        }

        if (changed)
            UpdateAllUi();
    }

    private void OnBracerUnlocked(Entity<CMUYautjaExperimentComponent> ent,
        ref CMUYautjaBracerUnlockedEvent args)
    {
        var changed = false;
        foreach (var (faction, state) in _missionStates.ToArray())
        {
            for (var i = state.Active.Count - 1; i >= 0; i--)
            {
                var mission = state.Active[i];
                if (mission.Definition.Kind != CompanyMissionKind.PredatorBracer)
                    continue;

                if (_timing.CurTime >= mission.Deadline)
                    continue;

                mission.Progress++;
                mission.Reward = mission.Progress;
                if (mission.Progress >= mission.Target)
                    CompleteMission(faction, state, i);

                changed = true;
            }
        }

        if (changed)
            UpdateAllUi();
    }

    private void UpdateMissionProgress()
    {
        var changed = false;
        foreach (var (faction, state) in _missionStates.ToArray())
        {
            if (state.Active.Count == 0)
                continue;

            for (var i = state.Active.Count - 1; i >= 0; i--)
            {
                var mission = state.Active[i];
                if (_timing.CurTime >= mission.Deadline)
                {
                    FailMission(faction, state, i);
                    changed = true;
                    continue;
                }

                switch (mission.Definition.Kind)
                {
                    case CompanyMissionKind.RoboticsPoints:
                    case CompanyMissionKind.ChemistryPoints:
                        var science = QueryScience(faction);
                        var current = mission.Definition.Kind == CompanyMissionKind.RoboticsPoints
                            ? science.RoboticsPoints
                            : science.ChemistryPoints;
                        var shown = Math.Min(current, mission.Target);
                        if (shown > mission.Progress)
                        {
                            mission.Progress = shown;
                            changed = true;
                        }

                        if (current >= mission.Target)
                        {
                            mission.Progress = mission.Target;
                            CompleteMission(faction, state, i);
                            changed = true;
                        }

                        break;
                    case CompanyMissionKind.CorruptedHiveRepresentative:
                        if (HasCorruptedHiveRepresentative())
                        {
                            mission.Progress = 1;
                            CompleteMission(faction, state, i);
                            changed = true;
                        }

                        break;
                    case CompanyMissionKind.CorruptedHiveAlliance:
                        if (HasCorruptedHiveAlliance())
                        {
                            mission.Progress = 1;
                            CompleteMission(faction, state, i);
                            changed = true;
                        }

                        break;
                }
            }
        }

        if (changed)
            UpdateAllUi();
    }

    private bool HasCorruptedHiveRepresentative()
    {
        var hiveQuery = EntityQueryEnumerator<HiveComponent>();
        while (hiveQuery.MoveNext(out var hiveUid, out var hive))
        {
            if (!hive.Corrupted)
                continue;

            if (hive.CurrentQueen is { } queen
                && !TerminatingOrDeleted(queen)
                && _mobState.IsAlive(queen))
            {
                return true;
            }

            var memberQuery = EntityQueryEnumerator<HiveMemberComponent, MobStateComponent>();
            while (memberQuery.MoveNext(out var memberUid, out var member, out _))
            {
                if (member.Hive != hiveUid || !_mobState.IsAlive(memberUid))
                    continue;

                if (HasComp<HiveLeaderComponent>(memberUid))
                    return true;

                var prototype = MetaData(memberUid).EntityPrototype?.ID;
                if (prototype != null &&
                    prototype.Contains("Overmind", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasCorruptedHiveAlliance()
    {
        var ally = new ProtoId<NpcFactionPrototype>("AUWeYu");
        var hiveQuery = EntityQueryEnumerator<HiveComponent>();
        while (hiveQuery.MoveNext(out var hiveUid, out var hive))
        {
            if (hive.Corrupted && _xenoHive.HasFaction(hiveUid, ally))
                return true;
        }

        return _npcFaction.IsFactionFriendly("AUWeYu", "CMU14PathogenWalker") ||
               _npcFaction.IsFactionFriendly("AUWeYu", "CMUCorruptedHive");
    }

    private void CompleteMission(string faction, FactionMissionState state, int index)
    {
        var mission = state.Active[index];
        state.Active.RemoveAt(index);
        state.CompletedIds.Add(mission.Definition.Id);
        state.History.Add(new MissionRecord
        {
            Definition = mission.Definition,
            Status = CompanyMissionStatus.Completed,
            Progress = mission.Progress,
            Target = mission.Target,
            Reward = mission.Reward,
            SecondsRemaining = 0,
        });

        state.CompanyValue += mission.Reward;

        if (IsPointsMission(mission.Definition.Kind))
        {
            state.PointsMissionCompletions[mission.Definition.Id] =
                state.PointsMissionCompletions.GetValueOrDefault(mission.Definition.Id) + 1;
        }

        state.Status = Loc.GetString(
            "cmu-company-mission-completed",
            ("name", Loc.GetString(mission.Definition.NameLoc)),
            ("reward", mission.Reward));
    }

    private void FailMission(string faction, FactionMissionState state, int index)
    {
        var mission = state.Active[index];
        state.Active.RemoveAt(index);
        state.FailedIds.Add(mission.Definition.Id);
        state.History.Add(new MissionRecord
        {
            Definition = mission.Definition,
            Status = CompanyMissionStatus.Failed,
            Progress = mission.Progress,
            Target = mission.Target,
            Reward = 0,
            SecondsRemaining = 0,
        });

        state.Status = Loc.GetString(
            "cmu-company-mission-failed",
            ("name", Loc.GetString(mission.Definition.NameLoc)));
    }

    private List<CompanyMissionInfo> BuildAvailableMissions(FactionMissionState state)
    {
        var available = new List<CompanyMissionInfo>();
        foreach (var definition in MissionCatalog)
        {
            if (!definition.Repeatable &&
                (state.CompletedIds.Contains(definition.Id) || state.FailedIds.Contains(definition.Id)))
                continue;
            if (state.Active.Any(mission => mission.Definition.Id == definition.Id))
                continue;
            if (IsScanMission(definition.Kind) && state.ScanMissionsStarted >= MaxScanMissionsPerRound)
                continue;

            var target = GetPointsMissionTarget(state, definition);
            var reward = definition.Kind == CompanyMissionKind.PredatorBracer
                ? definition.Target
                : definition.Reward;
            available.Add(BuildMissionInfo(definition, CompanyMissionStatus.Available, 0,
                target, reward, 0));
        }

        return available;
    }

    private List<CompanyMissionInfo> BuildActiveMissions(FactionMissionState state)
    {
        var active = new List<CompanyMissionInfo>();
        foreach (var mission in state.Active)
        {
            var seconds = Math.Max(0, (int) Math.Ceiling((mission.Deadline - _timing.CurTime).TotalSeconds));
            active.Add(BuildMissionInfo(mission.Definition, CompanyMissionStatus.Active, mission.Progress,
                mission.Target, mission.Reward, seconds));
        }

        return active;
    }

    private List<CompanyMissionInfo> BuildMissionHistory(FactionMissionState state)
    {
        var history = new List<CompanyMissionInfo>();
        foreach (var record in state.History)
        {
            history.Add(BuildMissionInfo(record.Definition, record.Status, record.Progress,
                record.Target, record.Reward, record.SecondsRemaining));
        }

        return history;
    }

    private CompanyMissionInfo BuildMissionInfo(
        CompanyMissionDefinition definition,
        CompanyMissionStatus status,
        int progress,
        int target,
        int reward,
        int secondsRemaining)
    {
        var objective = Loc.GetString(
            definition.ObjectiveLoc,
            ("target", target),
            ("minutes", definition.TimeLimitMinutes));

        return new CompanyMissionInfo(
            definition.Id,
            Loc.GetString(definition.NameLoc),
            Loc.GetString(definition.DescriptionLoc),
            objective,
            reward,
            progress,
            target,
            definition.TimeLimitMinutes,
            secondsRemaining,
            status);
    }

    private string DirectionName(CorporateResearchDirection direction)
    {
        return direction switch
        {
            CorporateResearchDirection.Robotics => Loc.GetString("cmu-company-direction-robotics"),
            CorporateResearchDirection.Bioengineering => Loc.GetString("cmu-company-direction-bioengineering"),
            _ => Loc.GetString("cmu-company-direction-none"),
        };
    }

    private static string GetConsoleFaction(EntityUid uid)
    {
        return CorporateFaction;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextUiRefresh)
        {
            _nextUiRefresh = _timing.CurTime + TimeSpan.FromSeconds(2);
            var query = EntityQueryEnumerator<CorporateConsoleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (_ui.IsUiOpen(uid, CorporateConsoleUi.Key))
                    UpdateUiState(uid, comp);
            }
        }

        if (_timing.CurTime < _nextMissionTick)
            return;

        _nextMissionTick = _timing.CurTime + TimeSpan.FromSeconds(1);
        UpdateMissionProgress();
    }
}
