using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Threats.Mobs.Wendigo.Lab;
using Content.Shared.CMU14.Yautja;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.ColonyEconomy;

// Mission progress hooks raised by science and laboratory events.
public sealed partial class CorporateConsoleSystem
{
    private void OnChemicalScanned(ref CMUChemicalScannedEvent args)
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
                CompanyMissionKind.ChemistryScanSimple => IsSimplePropertyScan(
                    args.Properties,
                    mission.ScanPropertyA,
                    mission.ScanPropertyB),
                CompanyMissionKind.ChemistryScanComplex => IsComplexPropertyScan(args.Properties),
                _ => false,
            };

            if (!matches)
                continue;

            if (_timing.CurTime >= mission.Deadline)
                continue;

            mission.Progress = 1;
            CompleteMission(state, i);
            changed = true;
        }

        if (changed)
            UpdateAllUi();
    }

    private static bool IsSimplePropertyScan(
        Dictionary<string, int> properties,
        string first,
        string second)
    {
        return !string.IsNullOrEmpty(first)
               && !string.IsNullOrEmpty(second)
               && properties.ContainsKey(first)
               && properties.ContainsKey(second);
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

    private void OnWendigoCreated(ref CMUWendigoCreatedEvent args)
    {
        var changed = false;
        foreach (var state in _missionStates.Values)
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
                CompleteMission(state, i);
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
        foreach (var state in _missionStates.Values)
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
                    CompleteMission(state, i);

                changed = true;
            }
        }

        if (changed)
            UpdateAllUi();
    }
}
