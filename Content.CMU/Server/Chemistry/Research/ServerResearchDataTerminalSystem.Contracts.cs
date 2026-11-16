using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Chemistry.Research;

// Contract legalization, nested sub-contracts and sample-data rewards.
public sealed partial class ServerResearchDataTerminalSystem
{
    /// <summary>
    /// Turns a contract offer into a live reagent and reaction. Returns false, registering nothing,
    /// when no non-colliding recipe could be generated for it.
    /// </summary>
    /// <param name="faction">The picking faction. Only faction picks can nest a sub-contract; admin and command
    /// contracts pass null and are legalized exactly as authored.</param>
    /// <param name="forceNested">Overrides the <see cref="NestedContractChance"/> roll for Hard contracts (tests).</param>
    /// <param name="reuseCompleted">Overrides the <see cref="ReuseCompletedSubContractChance"/> roll (tests).</param>
    public bool LegalizeChem(GeneratedReagentData chem, string? faction = null, bool? forceNested = null,
        bool? reuseCompleted = null)
    {
        chem.SubContractID = string.Empty;
        chem.SubContractIsNew = false;

        // ARCH-02: Hard picks may need another contract chemical ("Chem A") as one of their reactants.
        if (faction is not null && chem.GenTier >= 3)
        {
            var shouldNest = forceNested ?? _random.Prob(NestedContractChance);
            if (shouldNest && TryLegalizeNested(ref chem, faction, reuseCompleted))
                return true;
        }

        if (!TryGenerateContractRecipe(ref chem, null))
        {
            _sawmill.Error($"Could not generate a non-colliding recipe for research chemical {chem.ID}; contract dropped.");
            return false;
        }
        RegisterContractChem(chem);
        return true;
    }

    /// <summary>
    /// Generates a contract recipe containing its hint (and <paramref name="nested"/>, when given). The hint
    /// reagent may be what collides: Hard contracts retry with a different precious metal or botanical so they
    /// keep their guaranteed premium reactant; other tiers retry without a hint.
    /// </summary>
    private bool TryGenerateContractRecipe(ref GeneratedReagentData chem, string? nested)
    {
        HashSet<string> str = chem.RecipeHint == string.Empty ? [] : [chem.RecipeHint];
        if (nested is not null)
            str.Add(nested);
        if (_generator.GenerateRecipe(ref chem, str))
            return true;

        var retried = false;
        if (chem.GenTier >= 3)
        {
            var hintToSkip = chem.RecipeHint;
            var premium = _generator.ChemicalGenClassesList["H1"]
                .Concat(_generator.ChemicalGenClassesList[SharedReagentGeneratorSystem.PreciousMetalPool])
                .Where(id => id != hintToSkip)
                .ToList();
            for (var attempt = 0; attempt < 3 && premium.Count > 0 && !retried; attempt++)
            {
                var hint = _random.PickAndTake(premium);
                HashSet<string> required = [hint];
                if (nested is not null)
                    required.Add(nested);
                if (_generator.GenerateRecipe(ref chem, required))
                {
                    chem.RecipeHint = hint;
                    retried = true;
                }
            }
        }
        if (!retried && (nested is not null || str.Count == 0 || chem.GenTier >= 3 || !_generator.GenerateRecipe(ref chem, [])))
            return false;
        if (!retried)
            chem.RecipeHint = chem.Recipe.Keys.First();
        return true;
    }

    /// <summary>Loads a legalized contract chemical on the server and every client, and records it.</summary>
    private void RegisterContractChem(GeneratedReagentData chem)
    {
        _generator.ChemicalGenClassesList[ServerReagentGeneratorSystem.ContractChemicalPool].Add(chem.ID);
        foreach (var ef in chem.Effects)
        {
            _generator.CheckGeneratedProperties(ef.Key);
        }
        var ev = new GenerateReagentEvent(chem);
        RaiseLocalEvent(ev);
        RaiseNetworkEvent(ev);
        _generator.ProceduralReagentData.Add(chem.ID, chem);
    }

    /// <summary>
    /// ARCH-02: legalizes a Hard contract with a nested chemical as an extra reactant (at most
    /// <see cref="ServerReagentGeneratorSystem.MaxPreciousMetalParts"/> parts). A new sub-contract is generated
    /// first but only registered, before its parent, once the parent's recipe is known to work. Returns false,
    /// registering nothing and leaving the offer's recipe empty, when nesting is not possible.
    /// </summary>
    private bool TryLegalizeNested(ref GeneratedReagentData chem, string faction, bool? reuseCompleted)
    {
        if (!TryPickSubContract(faction, chem.ID, reuseCompleted, out var sub, out var isNew))
            return false;

        var originalHint = chem.RecipeHint;
        for (var attempt = 0; attempt < NestedRecipeAttempts; attempt++)
        {
            chem.Recipe.Clear();
            chem.RecipeHint = originalHint;
            if (!TryGenerateContractRecipe(ref chem, sub.ID))
                break;
            if (!chem.Recipe.TryGetValue(sub.ID, out var entry) || entry.Item2)
                continue;
            // A's reaction sits at the same generated priority as X's: if they share a reactant, mixing X's
            // ingredients could make A out of them instead, or the other way round.
            if (SubContractCollides(chem, sub))
                continue;
            if (entry.Item1 > ServerReagentGeneratorSystem.MaxPreciousMetalParts)
                chem.Recipe[sub.ID] = (ServerReagentGeneratorSystem.MaxPreciousMetalParts, false);

            chem.SubContractID = sub.ID;
            chem.SubContractIsNew = isNew;
            // CreateRecipe indexes every reactant, so A must be loaded before X.
            if (isNew)
                RegisterContractChem(sub);
            RegisterContractChem(chem);
            return true;
        }

        chem.Recipe.Clear();
        chem.RecipeHint = originalHint;
        chem.SubContractID = string.Empty;
        chem.SubContractIsNew = false;
        return false;
    }

    /// <summary>True when the nested chemical's recipe shares a reagent with the parent's other reactants.</summary>
    public static bool SubContractCollides(GeneratedReagentData parent, GeneratedReagentData sub)
    {
        foreach (var reagent in sub.Recipe.Keys)
        {
            if (reagent != sub.ID && parent.Recipe.ContainsKey(reagent))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Chooses Chem A: one of the faction's completed contract chemicals, or a newly rolled Easy/Intermediate
    /// sub-contract with its own stats and recipe (not yet registered).
    /// </summary>
    private bool TryPickSubContract(string faction, string parentId, bool? reuseCompleted,
        out GeneratedReagentData sub, out bool isNew)
    {
        var reagents = _protoman.GetInstances<ReagentPrototype>();
        var completed = new List<GeneratedReagentData>();
        foreach (var id in GetResearch(faction).CompletedChemicals)
        {
            if (id == parentId ||
                !_generator.ProceduralReagentData.TryGetValue(id, out var done) ||
                done.Class != ReagentClass.Ultra ||
                !string.IsNullOrEmpty(done.OriginalID) ||
                !string.IsNullOrEmpty(done.SubContractID) ||
                _generator.LockedDownChems.Contains(id) ||
                !reagents.ContainsKey(id))
            {
                continue;
            }
            completed.Add(done);
        }

        if (completed.Count > 0 && (reuseCompleted ?? _random.Prob(ReuseCompletedSubContractChance)))
        {
            sub = _random.Pick(completed);
            isNew = false;
            return true;
        }

        isNew = true;
        return TryRollSubContract(faction, out sub);
    }

    /// <summary>A new sub-contract: GenTier 1 or 2, so it can never nest further.</summary>
    private bool TryRollSubContract(string faction, out GeneratedReagentData sub)
    {
        sub = new GeneratedReagentData();
        sub.Recipe = [];
        sub.Effects = [];
        sub.Class = ReagentClass.Ultra;
        _generator.GenerateName(ref sub);
        sub.ID = $"{sub.ID}-{faction}-{_nextContractId++}";
        sub.GenTier = _random.Next(1, 3);
        _generator.GenerateStats(ref sub);
        _generator.RollTemperatureGate(ref sub);
        AssignContractRewardAndHint(ref sub);
        sub.PropertyHint = sub.Effects.Count > 0 ? _random.Pick(sub.Effects.Keys) : string.Empty;
        return TryGenerateContractRecipe(ref sub, null);
    }

    /// <summary>
    /// Marks a contract's nested chemical as identified, so the contract's own XRF report can name it instead of
    /// reporting an unknown emission.
    /// </summary>
    public void IdentifySubContract(string id)
    {
        if (!_generator.ProceduralReagentData.TryGetValue(id, out var data) || string.IsNullOrEmpty(data.SubContractID))
            return;
        var sub = data.SubContractID;
        var reward = _generator.ProceduralReagentData.TryGetValue(sub, out var subData) ? subData.ScanPointYield : 0;
        if (_generator.IdentifiedChemicals.TryAdd(sub, reward))
            RaiseNetworkEvent(new IdentifyChemicalEvent(sub, reward));
    }

    /// <summary>
    /// Registers an exact admin-authored chemical and prints a materializable contract for it.
    /// </summary>
    public EntityUid? IssueAdminContract(EntityUid source, GeneratedReagentData chem)
    {
        if (!LegalizeChem(chem))
            return null;
        return PrintContract(source, chem.ID, true);
    }

    /// <summary>
    /// Sample data paid for the first scan of a simulator-made chemical. Kept at the minimum simulation cost (1)
    /// so that simulate-then-scan can never be a net gain; contract and xeno scans are the real income.
    /// </summary>
    public static int SampleDataForSimulated(ReagentPrototype proto) => 1;

    /// <summary>
    /// FLOW-02: sample data paid for completing a contract chemical: 1 Easy, 2 Intermediate, 4 Hard. Credits are
    /// still the contract's full <see cref="ReagentPrototype.Reward"/>.
    /// </summary>
    public static int SampleDataForContract(ReagentPrototype proto)
        => proto.GenTier switch
        {
            >= 3 => HardContractSampleData,
            2 => IntermediateContractSampleData,
            _ => ContractSampleData,
        };

    /// <summary>
    /// Sample data paid for a faction's first completion of <paramref name="proto"/>: simulator output pays
    /// <see cref="SampleDataForSimulated"/>, generated contract chemicals (Ultra) pay
    /// <see cref="SampleDataForContract"/>, and other Special-or-better chemicals (xeno) pay their Reward.
    /// </summary>
    public static int SampleDataForCompleted(ReagentPrototype proto)
    {
        if (proto.Class < ReagentClass.Special)
            return SampleDataForSimulated(proto);
        if (proto.Generated && proto.Class == ReagentClass.Ultra)
            return SampleDataForContract(proto);
        return proto.Reward;
    }

}
