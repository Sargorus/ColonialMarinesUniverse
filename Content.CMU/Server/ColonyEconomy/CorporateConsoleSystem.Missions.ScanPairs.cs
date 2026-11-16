using System;
using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CMU14.ColonyEconomy;

// Random opposing property pairs for the simple chemistry scan mission.
public sealed partial class CorporateConsoleSystem
{
    private static readonly ProtoId<DatasetPrototype> ConflictingProperties = "CMUConflictingProperties";

    private bool TryChooseScanPair(FactionMissionState state, out string first, out string second)
    {
        first = string.Empty;
        second = string.Empty;

        if (!_proto.TryIndex(ConflictingProperties, out var dataset))
            return false;

        var candidates = new List<(string First, string Second)>();
        foreach (var value in dataset.Values)
        {
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !IsValidSimpleScanProperty(parts[0])
                || !IsValidSimpleScanProperty(parts[1]))
            {
                continue;
            }

            if (state.UsedScanPairs.Contains(PairKey(parts[0], parts[1])))
                continue;

            candidates.Add((parts[0], parts[1]));
        }

        if (candidates.Count == 0)
            return false;

        var pick = _random.Pick(candidates);
        first = pick.First;
        second = pick.Second;
        state.UsedScanPairs.Add(PairKey(first, second));
        return true;
    }

    private bool IsValidSimpleScanProperty(string id)
    {
        return _proto.TryIndex<ReagentPropertyPrototype>(id, out var property)
               && !property.Abstract
               && property.Rarity is ReagentPropertyRarityEnum.Common or ReagentPropertyRarityEnum.Uncommon;
    }

    private static string PairKey(string first, string second)
    {
        return string.CompareOrdinal(first, second) <= 0
            ? $"{first}|{second}"
            : $"{second}|{first}";
    }

    private string PropertyName(string id)
    {
        return _proto.TryIndex<ReagentPropertyPrototype>(id, out var property)
            ? property.LocalizedName
            : id;
    }
}
