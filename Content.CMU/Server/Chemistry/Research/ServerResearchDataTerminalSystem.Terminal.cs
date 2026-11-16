using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared._RMC14.Requisitions;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

// Terminal upgrades, cooldown purchases and UI state.
public sealed partial class ServerResearchDataTerminalSystem
{
    private int UpgradeCost(string faction)
    {
        var clearance = GetClearance(faction);
        return (_researchLevelIncreaseMult * clearance) + 1;
    }

    private void OnUpgradeAttempt(Entity<ResearchDataTerminalComponent> ent, ref ResearchDataTerminalAttemptUpgradeBuiMsg args)
    {
        var faction = ent.Comp.Faction;
        if (IsResearchBlocked(faction))
        {
            PopupBlocked(ent.Owner, args.Actor);
            UpdateUI(ent);
            return;
        }
        var clearance = GetClearance(faction);
        if (clearance == 5 && _ticker.RoundDuration() < XClearanceLockout)
        {
            UpdateUI(ent);
            return;
        }
        var cost = UpgradeCost(faction);
        if (clearance >= 6 || GetCredits(faction) < cost)
            return;
        UpdateClearance(GetCredits(faction) - cost, clearance + 1, faction);
        if (clearance == 5)
        {
            if (TryGetCipherElevator(ent.Owner, args.Actor, out var elevator))
            {
                elevator.Comp.Orders.Add(new RequisitionsEntry { Cost = 0, Crate = "CMUCrateSecureCipheringExperiment" });
                elevator.Comp.Orders.Add(new RequisitionsEntry { Cost = 0, Crate = "AU14CrateSecureWeYuBloodbursting" });
                SpawnNextToOrDrop("CMUCipherHintPaperInformDeliv", ent.Owner);
            }
            else
                SpawnNextToOrDrop("CMUCipherHintPaper", ent.Owner);
        }
        UpdateFactionUI(faction);
    }

    public bool ReduceCooldown(string faction)
    {
        if (IsResearchBlocked(faction))
            return false;
        var research = GetResearch(faction);
        if (!research.Picked || research.NextReroll <= _timer.CurTime || GetCredits(faction) < 1)
            return false;
        UpdateClearance(GetCredits(faction) - 1, -1, faction);
        research.NextReroll = TimeSpan.FromTicks(Math.Max(_timer.CurTime.Ticks,
            (research.NextReroll - TimeSpan.FromSeconds(60)).Ticks));
        if (research.NextReroll <= _timer.CurTime)
            RerollChems(faction);
        UpdateFactionUI(faction);
        return true;
    }

    private void OnReduceCooldown(Entity<ResearchDataTerminalComponent> ent, ref CMUResearchReduceCooldownBuiMsg args)
    {
        if (IsResearchBlocked(ent.Comp.Faction))
        {
            PopupBlocked(ent.Owner, args.Actor);
            UpdateUI(ent);
            return;
        }
        ReduceCooldown(ent.Comp.Faction);
    }


    private void OnUiOpen(Entity<ResearchDataTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUI(ent);
    }

    private void UpdateUI(Entity<ResearchDataTerminalComponent> ent)
    {
        var faction = ent.Comp.Faction;
        var research = GetResearch(faction);
        TimeSpan? xLockedUntil = null;
        if (GetClearance(faction) == 5 && _ticker.RoundDuration() < XClearanceLockout)
            xLockedUntil = _timer.CurTime + (XClearanceLockout - _ticker.RoundDuration());
        var contracts = new List<ResearchContractEntry>();
        foreach (var id in research.PickedContracts)
        {
            if (_generator.ProceduralReagentData.TryGetValue(id, out var contract))
                contracts.Add(new ResearchContractEntry(contract.Name, FormatContract(contract, true)));
        }
        var locked = IsResearchBlocked(faction);
        var catalog = new ResearchKnowledgeCatalogEvent(faction);
        RaiseLocalEvent(catalog);
        var state = new ResearchDataTerminalBuiState(
            ids: locked ? new List<GeneratedReagentData>() : research.Selectable.ToList(),
            data: new(research.Reports),
            nextUpdate: research.NextReroll,
            lastTime: research.LastTime,
            credits: GetCredits(faction),
            sampleData: GetSampleData(faction),
            clearance: GetClearance(faction),
            upgradecost: UpgradeCost(faction),
            xLockedUntil: xLockedUntil,
            picked: research.Picked,
            contracts: contracts,
            locked: locked,
            lockedReason: locked ? Loc.GetString("research-locked-insurgency") : null,
            guarantees: GetGuaranteeEntries(),
            guaranteedProperty: research.GuaranteedProperty,
            guaranteedPropertyCost: research.GuaranteedPropertyCost,
            knowledge: catalog.Entries);
        _ui.SetUiState(ent.Owner, ResearchDataTerminalUI.Key, state);
    }

    /// <summary>
    /// Atomically reserves one contract for a faction before contract generation raises further events.
    /// </summary>
}
