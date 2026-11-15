using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.ColonyEconomy;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleSystem
{
    private const string CorporateFaction = "corporate";
    private const int MaxActiveMissions = 3;
    private const int MaxScanMissionsPerRound = 3;
    private const int PointsMissionStep = 10;

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
        public string Faction = string.Empty;
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

    private readonly Dictionary<string, FactionMissionState> _missionStates =
        new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan _nextMissionTick;
    private TimeSpan _nextUiRefresh;

    private FactionMissionState GetMissionState(string faction)
    {
        if (_missionStates.TryGetValue(faction, out var state))
            return state;

        state = new FactionMissionState { Faction = faction };
        _missionStates[faction] = state;
        return state;
    }

    private CMUScienceStatusQueryEvent QueryScience(string faction)
    {
        var ev = new CMUScienceStatusQueryEvent(faction);
        RaiseLocalEvent(ref ev);
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

    private static string GetConsoleFaction(EntityUid uid)
    {
        return CorporateFaction;
    }
}
