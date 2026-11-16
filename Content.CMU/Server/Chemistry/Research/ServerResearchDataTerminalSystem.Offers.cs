using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Chemistry.Research;

// Contract offers, rerolls and pick reservation.
public sealed partial class ServerResearchDataTerminalSystem
{
    public bool TryReserveContract(string faction, string id, out GeneratedReagentData reagent)
    {
        var research = GetResearch(faction);
        var index = research.Selectable.FindIndex(candidate => candidate.ID == id);
        if (research.Picked || index < 0)
        {
            reagent = default;
            return false;
        }

        reagent = research.Selectable[index];
        research.Picked = true;
        research.NextReroll = _timer.CurTime + PickedRerollTime;
        research.LastTime = _timer.CurTime;
        research.Selectable.RemoveAt(index);

        // Push the lock before contract generation can raise any further events. Open windows on every
        // same-faction terminal are disabled together, and stale requests are rejected above.
        UpdateFactionUI(faction);
        return true;
    }

    public bool PickChem(string id, Entity<ResearchDataTerminalComponent>? ent = null)
    {
        var faction = ent?.Comp.Faction ?? "corporate";
        if (IsResearchBlocked(faction))
            return false;
        var research = GetResearch(faction);
        var nextReroll = research.NextReroll;
        var lastTime = research.LastTime;
        if (!TryReserveContract(faction, id, out var reagent))
            return false;

        if (!LegalizeChem(reagent, faction))
        {
            // Give the offer slot and the reroll timer back rather than leaving the faction locked on a dead contract.
            research.Picked = false;
            research.NextReroll = nextReroll;
            research.LastTime = lastTime;
            research.Selectable.Add(reagent);
            UpdateFactionUI(faction);
            return false;
        }
        research.PickedContracts.Add(reagent.ID);
        if (_generator.ProceduralReagentData.TryGetValue(reagent.ID, out var legalized) &&
            legalized.SubContractIsNew && !string.IsNullOrEmpty(legalized.SubContractID))
        {
            research.PickedContracts.Add(legalized.SubContractID);
        }
        if (ent is { } terminal)
            PrintContract(terminal, reagent.ID);
        UpdateFactionUI(faction);
        return true;
    }

    private void OnPickChem(Entity<ResearchDataTerminalComponent> ent, ref ResearchDataTerminalPickChemBuiMsg args)
    {
        if (IsResearchBlocked(ent.Comp.Faction))
            PopupBlocked(ent.Owner, args.Actor);
        else
            PickChem(args.Pick, ent);
        UpdateUI(ent);
    }

    private void RerollChems(string faction)
    {
        var research = GetResearch(faction);
        research.Selectable.Clear();
        research.Picked = false;
        if (IsResearchBlocked(faction))
        {
            // FLOW-24: no offers and no announcement for a locked faction; check again after the usual wait.
            research.NextReroll = _timer.CurTime + RerollTime;
            research.LastTime = _timer.CurTime;
            UpdateFactionUI(faction);
            return;
        }
        for (int i = 0; i < ResearchChemAmount; i++)
        {
            GeneratedReagentData data = new();
            data.Recipe = [];
            data.Effects = [];
            data.Class = ReagentClass.Ultra;
            _generator.GenerateName(ref data);
            // Offers are generated before registration; reserve a unique ID across faction pools.
            data.ID = $"{data.ID}-{faction}-{_nextContractId++}";
            data.GenTier = _random.Next(1, 4);
            _generator.GenerateStats(ref data);
            _generator.RollTemperatureGate(ref data);
            AssignContractRewardAndHint(ref data);
            data.PropertyHint = _random.Pick(data.Effects.Keys);
            research.Selectable.Add(data);
        }
        ApplyGuaranteeToFirstOffer(research);
        research.NextReroll = _timer.CurTime + RerollTime;
        research.LastTime = _timer.CurTime;
        UpdateFactionUI(faction, announce: true);
    }

    /// <summary>Sets a contract's credit reward (3/5/7) and the reagent hint pinned into its recipe, by tier.</summary>
    private void AssignContractRewardAndHint(ref GeneratedReagentData data)
    {
        var roll = _random.Next(1, 101);
        switch (data.GenTier)
        {
            case 1:
                data.ScanPointYield = 3;
                data.RecipeHint = _random.Pick(_generator.ChemicalGenClassesList["C1"]);
                break;
            case 2:
                data.ScanPointYield = 5;
                data.RecipeHint = _random.Pick(_generator.ChemicalGenClassesList[roll <= 50 ? "C2" : "C3"]);
                break;
            case 3:
                // Hard contracts always pin either a botanical or a precious metal.
                data.ScanPointYield = 7;
                var precious = _generator.ChemicalGenClassesList[SharedReagentGeneratorSystem.PreciousMetalPool];
                data.RecipeHint = roll <= 60 || precious.Count == 0
                    ? _random.Pick(_generator.ChemicalGenClassesList["H1"])
                    : _random.Pick(precious);
                break;
            default:
                data.ScanPointYield = 3;
                data.RecipeHint = _random.Pick(_generator.ChemicalGenClassesList["C1"]);
                break;
        }
    }
}
