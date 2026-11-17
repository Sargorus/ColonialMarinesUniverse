using System;
using System.Collections.Generic;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Server.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Chemistry.Reagent;
using Content.Shared._RMC14.Requisitions;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

// Chemical discovery and faction-specific rewards.
public sealed partial class ServerResearchDataTerminalSystem
{
    [Dependency] private CorporateConsoleSystem _corpo = default!;
    [Dependency] private ColonyBudgetSystem _colbud = default!;
    [Dependency] private SharedRequisitionsSystem _reqsys = default!;

    /// <summary>
    /// Legacy entry point: records the discovery and pays its reward. The in-game XRF scanner only records the
    /// discovery; the reward is paid when a research document is submitted at a terminal.
    /// </summary>
    public void CompleteChemical(ReagentPrototype proto, string faction, EntityUid? scanner)
    {
        DiscoverChemical(proto, faction);
        GrantChemicalReward(proto, faction, scanner);
    }

    /// <summary>
    /// Records a faction's first discovery of a chemical and publishes the scan event. This does not pay
    /// credits, sample data or money; those are document rewards.
    /// </summary>
    public bool DiscoverChemical(ReagentPrototype proto, string faction)
    {
        // FLOW-24: a locked faction earns nothing and the chemical is not marked completed for it.
        if (IsResearchBlocked(faction))
            return false;
        if (!GetResearch(faction).CompletedChemicals.Add(proto.ID))
            return false;
        // Chemical definitions are global, but each faction records its own first scan.
        if (_generator.IdentifiedChemicals.TryAdd(proto.ID, proto.Reward))
            RaiseNetworkEvent(new IdentifyChemicalEvent(proto.ID, proto.Reward));
        // ARCH-02: completing a nested contract also identifies its sub-chemical (no separate reward).
        IdentifySubContract(proto.ID);
        var scannedProperties = _generator.ProceduralReagentData.TryGetValue(proto.ID, out var scanned)
            ? new Dictionary<string, int>(scanned.Effects)
            : new Dictionary<string, int>();
        var scannedEvent = new CMUChemicalScannedEvent(faction, proto.ID, scannedProperties);
        RaiseLocalEvent(ref scannedEvent);
        return true;
    }

    /// <summary>
    /// Pays a faction's first reward for a discovered chemical. Kept separate so document submission can use
    /// the same faction economy without paying twice at scan time.
    /// </summary>
    public void GrantChemicalReward(ReagentPrototype proto, string faction, EntityUid? scanner)
    {
        if (IsResearchBlocked(faction))
            return;
        if (!GetResearch(faction).RewardedChemicals.Add(proto.ID))
            return;
        if (proto.Class < ReagentClass.Special)
        {
            // Simulator-made chemical: a flat sample-data reward (see SampleDataForSimulated), no credits or cash.
            UpdateSampleData(GetSampleData(faction) + SampleDataForSimulated(proto), faction);
            return;
        }
        UpdateClearance(
            GetCredits(faction) + proto.Reward,
            -1,
            faction,
            GetSampleData(faction) + SampleDataForCompleted(proto));
        PayFactionCash(faction, (int)MathF.Round(ResearchCashRewardMult * proto.Reward), scanner);
    }

    /// <summary>
    /// Research direction chosen at the liaison console. Credit payouts use it as a specialization
    /// modifier: robotics multiplies credits, bioengineering improves chemistry contract credits.
    /// </summary>
    public CorporateResearchDirection GetResearchDirection(string faction)
        => _corpo.GetResearchDirection(faction);

    /// <summary>
    /// Routes research cash to the owning faction account. An unknown faction drops physical cash at the
    /// scanner, preserving the old scanner fallback for admin and unassigned research.
    /// </summary>
    public void PayFactionCash(string faction, int amount, EntityUid? scanner)
    {
        if (amount <= 0)
            return;
        if (string.Equals(faction, "corporate", StringComparison.OrdinalIgnoreCase))
        {
            _corpo.AddToCorporateBudget(amount);
            return;
        }
        if (string.Equals(faction, "colony", StringComparison.OrdinalIgnoreCase))
        {
            _colbud.AddToBudget(amount);
            return;
        }
        if (string.Equals(faction, "govfor", StringComparison.OrdinalIgnoreCase))
        {
            _reqsys.ChangeBudget(amount, "govfor");
            return;
        }
        if (string.Equals(faction, "opfor", StringComparison.OrdinalIgnoreCase))
        {
            _reqsys.ChangeBudget(amount, "opfor");
            return;
        }
        if (scanner is null)
            return;
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
