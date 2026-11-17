using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.ColonyEconomy;

namespace Content.Server.CMU14.ColonyEconomy;

// Mission board information rendered by the liaison console.
public sealed partial class CorporateConsoleSystem
{
    private List<CompanyMissionInfo> BuildAvailableMissions(FactionMissionState state)
    {
        var available = new List<CompanyMissionInfo>();
        foreach (var definition in MissionCatalog)
        {
            if (!definition.Repeatable
                && (state.CompletedIds.Contains(definition.Id) || state.FailedIds.Contains(definition.Id)))
                continue;
            if (state.Active.Any(mission => mission.Definition.Id == definition.Id))
                continue;
            if (IsScanMission(definition.Kind) && state.ScanMissionsStarted >= MaxScanMissionsPerRound)
                continue;

            var target = GetPointsMissionTarget(state, definition);
            var reward = definition.Reward;
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
                mission.Target, mission.Reward, seconds,
                mission.ScanPropertyA, mission.ScanPropertyB));
        }

        return active;
    }

    private List<CompanyMissionInfo> BuildMissionHistory(FactionMissionState state)
    {
        var history = new List<CompanyMissionInfo>();
        foreach (var record in state.History)
        {
            history.Add(BuildMissionInfo(record.Definition, record.Status, record.Progress,
                record.Target, record.Reward, record.SecondsRemaining,
                record.ScanPropertyA, record.ScanPropertyB));
        }

        return history;
    }

    private CompanyMissionInfo BuildMissionInfo(
        CompanyMissionDefinition definition,
        CompanyMissionStatus status,
        int progress,
        int target,
        int reward,
        int secondsRemaining,
        string scanPropertyA = "",
        string scanPropertyB = "")
    {
        var objective = !string.IsNullOrEmpty(scanPropertyA) && !string.IsNullOrEmpty(scanPropertyB)
            ? Loc.GetString(
                "cmu-company-mission-scan-pair-objective",
                ("first", PropertyName(scanPropertyA)),
                ("second", PropertyName(scanPropertyB)),
                ("minutes", definition.TimeLimitMinutes))
            : Loc.GetString(
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
}
