using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.CMU14.Ops.ThirdParty;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Threats;
using Content.Shared.CMU14.Threats.Mobs.Wendigo.Lab;
using Content.Shared.CMU14.Yautja;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.HiveLeader;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleSystem
{
    private const string CorporateFaction = "corporate";
    private const string PmcPartyId = "CMUWYPMCTeamSmall";
    private const int MaxActiveMissions = 3;

    private static readonly ProtoId<ThirdPartyPrototype> PmcParty = new(PmcPartyId);

    private enum CompanyMissionKind : byte
    {
        RoboticsPoints,
        ChemistryPoints,
        ChemistryScanSimple,
        ChemistryScanComplex,
        CreateWendigo,
        CorruptedHiveRepresentative,
        CorruptedHiveAlliance,
        PredatorCuffs,
    }

    private sealed record CompanyMissionDefinition(
        string Id,
        CompanyMissionKind Kind,
        string NameLoc,
        string DescriptionLoc,
        string ObjectiveLoc,
        int Reward,
        int Target,
        int DeadlineMinute);

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

        public readonly List<ActiveCompanyMission> Active = new();
        public readonly List<MissionRecord> History = new();
        public readonly HashSet<string> CompletedIds = new(StringComparer.Ordinal);
        public readonly HashSet<string> FailedIds = new(StringComparer.Ordinal);
        public string Status = string.Empty;
    }

    /// <summary>
    /// The basic company mission board. Values are intentionally in one place so balance passes do not have to
    /// hunt through UI code: targets are absolute balances, deadlines are round minutes, and rewards are chemistry
    /// science points unless a mission overrides them dynamically (Wendigo variants and predator cuffs).
    /// </summary>
    private static readonly CompanyMissionDefinition[] MissionCatalog =
    [
        new(
            "robotics-points",
            CompanyMissionKind.RoboticsPoints,
            "cmu-company-mission-robotics-name",
            "cmu-company-mission-robotics-desc",
            "cmu-company-mission-robotics-objective",
            Reward: 15,
            Target: 25,
            DeadlineMinute: 60),
        new(
            "chemistry-points",
            CompanyMissionKind.ChemistryPoints,
            "cmu-company-mission-chemistry-name",
            "cmu-company-mission-chemistry-desc",
            "cmu-company-mission-chemistry-objective",
            Reward: 15,
            Target: 20,
            DeadlineMinute: 60),
        new(
            "chemistry-scan-simple",
            CompanyMissionKind.ChemistryScanSimple,
            "cmu-company-mission-scan-simple-name",
            "cmu-company-mission-scan-simple-desc",
            "cmu-company-mission-scan-objective",
            Reward: 10,
            Target: 1,
            DeadlineMinute: 45),
        new(
            "chemistry-scan-complex",
            CompanyMissionKind.ChemistryScanComplex,
            "cmu-company-mission-scan-complex-name",
            "cmu-company-mission-scan-complex-desc",
            "cmu-company-mission-scan-objective",
            Reward: 25,
            Target: 1,
            DeadlineMinute: 75),
        new(
            "create-wendigo",
            CompanyMissionKind.CreateWendigo,
            "cmu-company-mission-wendigo-name",
            "cmu-company-mission-wendigo-desc",
            "cmu-company-mission-wendigo-objective",
            Reward: 20,
            Target: 1,
            DeadlineMinute: 90),
        new(
            "corrupted-hive-representative",
            CompanyMissionKind.CorruptedHiveRepresentative,
            "cmu-company-mission-hive-rep-name",
            "cmu-company-mission-hive-rep-desc",
            "cmu-company-mission-single-objective",
            Reward: 25,
            Target: 1,
            DeadlineMinute: 90),
        new(
            "corrupted-hive-alliance",
            CompanyMissionKind.CorruptedHiveAlliance,
            "cmu-company-mission-hive-alliance-name",
            "cmu-company-mission-hive-alliance-desc",
            "cmu-company-mission-single-objective",
            Reward: 30,
            Target: 1,
            DeadlineMinute: 90),
        new(
            "predator-cuffs",
            CompanyMissionKind.PredatorCuffs,
            "cmu-company-mission-cuffs-name",
            "cmu-company-mission-cuffs-desc",
            "cmu-company-mission-cuffs-objective",
            Reward: 1,
            Target: 10,
            DeadlineMinute: 75),
    ];

    [Dependency] private SharedGameTicker _ticker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedXenoHiveSystem _xenoHive = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;

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

        if (state.CompletedIds.Contains(definition.Id) || state.FailedIds.Contains(definition.Id))
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

        if (_ticker.RoundDuration().TotalMinutes >= definition.DeadlineMinute)
        {
            state.Status = Loc.GetString(
                "cmu-company-mission-too-late",
                ("minute", definition.DeadlineMinute));
            UpdateAllUi();
            return;
        }

        var active = new ActiveCompanyMission
        {
            Definition = definition,
            Deadline = TimeSpan.FromMinutes(definition.DeadlineMinute),
            Target = definition.Target,
            Reward = definition.Reward,
        };

        if (definition.Kind is CompanyMissionKind.RoboticsPoints or CompanyMissionKind.ChemistryPoints)
        {
            var science = QueryScience(faction);
            var current = definition.Kind == CompanyMissionKind.RoboticsPoints
                ? science.RoboticsPoints
                : science.ChemistryPoints;
            active.Progress = Math.Min(current, active.Target);
        }

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
        var science = QueryScience(faction);
        if (science.ChemistryPoints < comp.PmcCostChemistry)
        {
            _ui.SetUiState(uid, CorporateConsoleUi.Key, BuildUiState(uid, comp));
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

        RaiseLocalEvent(new CMUAddSciencePointsEvent(faction, -comp.PmcCostChemistry));

        var query = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var other))
        {
            other.PmcCalls++;
            other.NextPmcCallAt = _timing.CurTime + other.PmcCooldown;
            _ui.SetUiState(consoleUid, CorporateConsoleUi.Key, BuildUiState(consoleUid, other));
        }
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

    private void OnCuffRemoved(CuffRemovedEvent args)
    {
        if (!IsPredatorCuff(args.Cuff))
            return;

        var changed = false;
        foreach (var (faction, state) in _missionStates.ToArray())
        {
            for (var i = state.Active.Count - 1; i >= 0; i--)
            {
                var mission = state.Active[i];
                if (mission.Definition.Kind != CompanyMissionKind.PredatorCuffs)
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

    private bool IsPredatorCuff(EntityUid cuff)
    {
        if (HasComp<CMUXenoRestraintsComponent>(cuff))
            return true;

        if (!TryComp<MetaDataComponent>(cuff, out var meta) || meta.EntityPrototype is not { } prototype)
            return false;

        var id = prototype.ID;
        return id.StartsWith("CMUHunterShipPlacedHandcuffs", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("CMUHunterShipPlacedZipties", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "CMUYautjaStrongXenoRestraints", StringComparison.OrdinalIgnoreCase);
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

        if (mission.Reward > 0)
            RaiseLocalEvent(new CMUAddSciencePointsEvent(faction, mission.Reward));

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
            if (state.CompletedIds.Contains(definition.Id) || state.FailedIds.Contains(definition.Id))
                continue;
            if (state.Active.Any(mission => mission.Definition.Id == definition.Id))
                continue;

            var reward = definition.Kind == CompanyMissionKind.PredatorCuffs
                ? definition.Target
                : definition.Reward;
            available.Add(BuildMissionInfo(definition, CompanyMissionStatus.Available, 0,
                definition.Target, reward, 0));
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
        var objective = definition.ObjectiveLoc switch
        {
            "cmu-company-mission-robotics-objective" or "cmu-company-mission-chemistry-objective" =>
                Loc.GetString(definition.ObjectiveLoc, ("target", target), ("minute", definition.DeadlineMinute)),
            "cmu-company-mission-scan-objective" =>
                Loc.GetString(definition.ObjectiveLoc, ("minute", definition.DeadlineMinute)),
            "cmu-company-mission-wendigo-objective" or "cmu-company-mission-cuffs-objective" =>
                Loc.GetString(definition.ObjectiveLoc, ("target", target), ("minute", definition.DeadlineMinute)),
            _ => Loc.GetString(definition.ObjectiveLoc, ("minute", definition.DeadlineMinute)),
        };

        return new CompanyMissionInfo(
            definition.Id,
            Loc.GetString(definition.NameLoc),
            Loc.GetString(definition.DescriptionLoc),
            objective,
            reward,
            progress,
            target,
            definition.DeadlineMinute,
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
