using System;
using System.Linq;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Database;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.ColonyEconomy;

// Mission selection, acceptance, completion and failure.
public sealed partial class CorporateConsoleSystem
{
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
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor)} set the {faction} research direction to " +
            $"{DirectionName(msg.Direction)} at {ToPrettyString(uid)}");
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

        if (!definition.Repeatable
            && (state.CompletedIds.Contains(definition.Id) || state.FailedIds.Contains(definition.Id)))
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
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor)} accepted the {faction} company mission " +
            $"{definition.Id} at {ToPrettyString(uid)}");
        UpdateAllUi();
    }

    private void CompleteMission(FactionMissionState state, int index)
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
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"The {state.Faction} company mission {mission.Definition.Id} completed " +
            $"and paid {mission.Reward} company value");
    }

    private void FailMission(FactionMissionState state, int index)
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
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"The {state.Faction} company mission {mission.Definition.Id} failed");
    }
}
