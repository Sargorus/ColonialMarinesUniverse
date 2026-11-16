using System;
using Content.Shared.CMU14.ColonyEconomy;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.ColonyEconomy;

// Periodic mission evaluation and console refresh.
public sealed partial class CorporateConsoleSystem
{
    private void UpdateMissionProgress()
    {
        var changed = false;
        foreach (var (faction, state) in _missionStates)
        {
            if (state.Active.Count == 0)
                continue;

            for (var i = state.Active.Count - 1; i >= 0; i--)
            {
                var mission = state.Active[i];
                if (_timing.CurTime >= mission.Deadline)
                {
                    FailMission(state, i);
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
                        var earned = Math.Max(0, current - mission.StartingPoints);
                        var shown = Math.Min(earned, mission.Target);
                        if (shown > mission.Progress)
                        {
                            mission.Progress = shown;
                            changed = true;
                        }

                        if (mission.Progress >= mission.Target)
                        {
                            mission.Progress = mission.Target;
                            CompleteMission(state, i);
                            changed = true;
                        }

                        break;
                    case CompanyMissionKind.CorruptedHiveRepresentative:
                        if (HasCorruptedHiveRepresentative())
                        {
                            mission.Progress = 1;
                            CompleteMission(state, i);
                            changed = true;
                        }

                        break;
                    case CompanyMissionKind.CorruptedHiveAlliance:
                        if (HasCorruptedHiveAlliance())
                        {
                            mission.Progress = 1;
                            CompleteMission(state, i);
                            changed = true;
                        }

                        break;
                }
            }
        }

        if (changed)
            UpdateAllUi();
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
