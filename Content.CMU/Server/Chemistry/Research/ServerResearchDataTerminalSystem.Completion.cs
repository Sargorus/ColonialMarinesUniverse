using System;
using System.Collections.Generic;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Server.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.Chemistry.Reagent;
using Content.Shared._RMC14.Requisitions;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

// Chemical completion and faction-specific cash rewards.
public sealed partial class ServerResearchDataTerminalSystem
{
    [Dependency] private CorporateConsoleSystem _corpo = default!;
    [Dependency] private ColonyBudgetSystem _colbud = default!;
    [Dependency] private SharedRequisitionsSystem _reqsys = default!;

    public void CompleteChemical(ReagentPrototype proto, string faction, EntityUid? scanner)
    {
        // FLOW-24: a locked faction earns nothing (no credits, sample data, budget or identification), and the
        // chemical is not marked completed for it.
        if (IsResearchBlocked(faction))
            return;
        if (!GetResearch(faction).CompletedChemicals.Add(proto.ID))
            return;
        // Chemical definitions are global, but each faction earns its own first-scan reward.
        if (_generator.IdentifiedChemicals.TryAdd(proto.ID, proto.Reward))
            RaiseNetworkEvent(new IdentifyChemicalEvent(proto.ID, proto.Reward));
        // ARCH-02: completing a nested contract also identifies its sub-chemical (no separate reward).
        IdentifySubContract(proto.ID);
        var scannedProperties = _generator.ProceduralReagentData.TryGetValue(proto.ID, out var scanned)
            ? new Dictionary<string, int>(scanned.Effects)
            : new Dictionary<string, int>();
        var scannedEvent = new CMUChemicalScannedEvent(faction, proto.ID, scannedProperties);
        RaiseLocalEvent(ref scannedEvent);
        if (proto.Class < ReagentClass.Special)
        {
            // Simulator-made chemical: a flat sample-data reward (see SampleDataForSimulated), no credits or cash.
            UpdateSampleData(GetSampleData(faction) + SampleDataForSimulated(proto), faction);
            return;
        }
        UpdateClearance(GetCredits(faction) + proto.Reward, -1, faction, GetSampleData(faction) + SampleDataForCompleted(proto));
        if (faction != string.Empty)
        {
            if (string.Equals(faction, "corporate", StringComparison.OrdinalIgnoreCase))
            {
                _corpo.AddToCorporateBudget(ResearchCashRewardMult * proto.Reward);
                return;
            }
            if (string.Equals(faction, "colony", StringComparison.OrdinalIgnoreCase))
            {
                _colbud.AddToBudget(ResearchCashRewardMult * proto.Reward);
                return;
            }
            if (string.Equals(faction, "govfor", StringComparison.OrdinalIgnoreCase))
            {
                _reqsys.ChangeBudget((int)MathF.Round(ResearchCashRewardMult * proto.Reward), "govfor");
                return;
            }
            if (string.Equals(faction, "opfor", StringComparison.OrdinalIgnoreCase))
            {
                _reqsys.ChangeBudget((int)MathF.Round(ResearchCashRewardMult * proto.Reward), "opfor");
                return;
            }
        }
        if (scanner is null) // guess they don't *really* need that money then
            return;
        int amount = (int)MathF.Round(ResearchCashRewardMult * proto.Reward);
        while (amount > 0)
        {
            switch (amount)
            {
                case >= 1000:
                    SpawnNextToOrDrop("RMCSpaceCash1000", scanner.Value);
                    amount -= 1000;
                    break;
                case >= 100:
                    SpawnNextToOrDrop("RMCSpaceCash100", scanner.Value);
                    amount -= 100;
                    break;
                case >= 10:
                    SpawnNextToOrDrop("RMCSpaceCash10", scanner.Value);
                    amount -= 10;
                    break;
                default:
                    SpawnNextToOrDrop("RMCSpaceCash", scanner.Value);
                    amount--;
                    break;
            }
        }
    }

}
