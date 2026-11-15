using System.Collections.Generic;
using System.Linq;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Chemistry.Research;

// Contract, report and analysis paper printing for the research data terminal.
public sealed partial class ServerResearchDataTerminalSystem : SharedResearchDataTerminalSystem
{
    private void OnPrintLast(Entity<ResearchDataTerminalComponent> ent, ref ResearchDataTerminalPrintLastBuiMsg args)
    {

        _printingLast.Add(ent);
    }

    private void OnPrintRequest(Entity<ResearchDataTerminalComponent> ent, ref ResearchDataTerminalPrintChemBuiMsg args)
    {
        _printing[ent] = args.Index;
    }

    private void PrintLast(Entity<ResearchDataTerminalComponent> ent)
    {
        var research = GetResearch(ent.Comp.Faction);
        if (research.LastPickName == string.Empty || research.LastPick == string.Empty)
            return;
        string name = Loc.GetString("research-data-synthesis-name", ("NAME", research.LastPickName));
        var paper = SpawnNextToOrDrop("CMUWYPaper", ent.Owner);
        _mets.SetEntityName(paper, name);
        //unparity, because the experiment number is re-randomized
        _paper.SetContent(paper, research.LastPick);
        RemCompDeferred<ResearchReportComponent>(paper);
    }

    private void PrintContract(Entity<ResearchDataTerminalComponent> ent, string id)
    {
        PrintContract(ent.Owner, id, false);
    }

    private EntityUid? PrintContract(EntityUid source, string id, bool materializable)
    {
        var dat = _generator.ProceduralReagentData[id];
        string name = Loc.GetString("research-data-contract-name", ("NAME", dat.Name));

        string expnum = string.Empty;
        List<string> exppre = new List<string>{ "C", "Q", "V", "W", "X", "Y", "Z" };
        expnum += _random.Pick(exppre);
        expnum += _random.Next(100, 1000);
        // The paper keeps hiding the property profile except on admin (materializable) contracts.
        var text = FormatContract(dat, materializable, expnum);
        var paperPrototype = materializable ? "CMUAdminChemicalContract" : "CMUResearchContract";
        var paper = SpawnNextToOrDrop(paperPrototype, source);
        _mets.SetEntityName(paper, name);
        _paper.SetContent(paper, text);
        if (materializable && TryComp<ResearchReportComponent>(paper, out var report))
        {
            report.Data = dat;
            report.Valid = true;
            report.Completed = true;
            DirtyEntity(paper);
        }
        if (!materializable)
        {
            var research = GetResearch(GetFaction(source));
            research.LastPickName = dat.Name;
            research.LastPick = text;
        }
        return paper;
    }

    /// <summary>
    /// ARCH-03: the contract text shared by the printed contract paper and the terminal's Contracts tab: the
    /// difficulty, the recipe and catalysts, a new sub-contract's own recipe, and optionally the property profile.
    /// With an <paramref name="experiment"/> number the text is the full paper, with the Weyland-Yutani header
    /// and footer; without one it is the tab entry.
    /// </summary>
    public string FormatContract(GeneratedReagentData dat, bool includeProperties, string? experiment = null)
    {
        var text = string.Empty;
        if (experiment is not null)
        {
            text += Loc.GetString("cmu-paper-header-experiment") + '\n';
            text += Loc.GetString("cmu-paper-subheader-experiment", ("NAME", dat.Name)) + '\n';
        }
        text += Loc.GetString("research-report-contract-difficulty", ("DIFF", DifficultyName(dat.GenTier))) + '\n';
        text += experiment is not null
            ? Loc.GetString("cmu-paper-contract-experiment", ("EXP", experiment), ("NAME", dat.Name)) + '\n'
            : Loc.GetString("research-report-contract-reagents", ("NAME", dat.Name)) + '\n';
        AppendRecipe(ref text, dat);
        AppendProcessRequirements(ref text, dat);

        if (dat.SubContractIsNew && !string.IsNullOrEmpty(dat.SubContractID) &&
            _generator.ProceduralReagentData.TryGetValue(dat.SubContractID, out var sub))
        {
            text += Loc.GetString("research-report-subcontract-header", ("NAME", sub.Name)) + '\n';
            text += Loc.GetString(
                "research-report-subcontract-reagents",
                ("NAME", sub.Name),
                ("PARENT", dat.Name)) + '\n';
            AppendRecipe(ref text, sub);
            AppendProcessRequirements(ref text, sub);
        }

        if (includeProperties)
        {
            text += Loc.GetString("admin-chemical-contract-properties-header") + '\n';
            var properties = _protoman.GetInstances<ReagentPropertyPrototype>();
            foreach (var property in dat.Effects.OrderBy(effect =>
                         properties.TryGetValue(effect.Key, out var p) ? p.LocalizedName : effect.Key))
            {
                var propertyName = properties.TryGetValue(property.Key, out var proto)
                    ? proto.LocalizedName
                    : property.Key;
                text += Loc.GetString(
                    "admin-chemical-contract-property-entry",
                    ("name", propertyName),
                    ("level", property.Value)) + '\n';
            }
        }

        if (experiment is not null)
            text += Loc.GetString("cmu-paper-contract-footer") + '\n';
        return text;
    }

    /// <summary>The Mix, heat/cooling gate and ignition-ceiling lines for a contract
    /// (or sub-contract) recipe.</summary>
    private void AppendProcessRequirements(ref string text, GeneratedReagentData data)
        => text += _generator.ProcessRequirementsText(data);

    /// <summary>Appends a contract's reactant lines, then its catalysts under their own header.</summary>
    private void AppendRecipe(ref string text, GeneratedReagentData data)
    {
        var reagents = _protoman.GetInstances<ReagentPrototype>();
        List<string> ingredients = new();
        List<string> catalysts = new();
        foreach (var ingredient in data.Recipe)
        {
            var reagentName = reagents.TryGetValue(ingredient.Key, out var reagent)
                ? reagent.LocalizedName
                : _generator.ProceduralReagentData.TryGetValue(ingredient.Key, out var generated)
                    ? generated.Name
                    : ingredient.Key;
            var line = Loc.GetString("research-report-ingredient", ("AMOUNT", ingredient.Value.Item1),
                ("NAME", reagentName)) + '\n';
            if (ingredient.Value.Item2)
                catalysts.Add(line);
            else
                ingredients.Add(line);
        }
        foreach (var str in ingredients)
        {
            text += str;
        }
        if (catalysts.Count > 0)
        {
            text += Loc.GetString("research-chem-catalyst") + '\n';
            foreach (var str in catalysts)
            {
                text += str;
            }
        }
    }

    private string DifficultyName(int tier) => tier switch
    {
        >= 3 => Loc.GetString("research-data-ui-diff-hard"),
        2 => Loc.GetString("research-data-ui-diff-inter"),
        _ => Loc.GetString("research-data-ui-diff-easy"),
    };
    private void PrintData(Entity<ResearchDataTerminalComponent> ent, int idx)
    {
        if (GetResearch(ent.Comp.Faction).Reports.TryGetValue(idx, out var value))
        {
            string name = string.Empty;
            if (value.Item4)
            {
                name = Loc.GetString("research-report-simulation-name", ("ID", value.Item5.Name));
            }
            else
            {
                name = Loc.GetString(
                    "research-report-analysis-name",
                    ("NAME1", value.Item5.Name),
                    ("NAME2", string.Empty));
            }
            var paper = SpawnNextToOrDrop("CMUWYPaper", ent.Owner);
            _mets.SetEntityName(paper, name);
            _paper.SetContent(paper, value.Item2);
            var datcomp = EnsureComp<ResearchReportComponent>(paper);
            datcomp.Valid = value.Item6;
            datcomp.Completed = value.Item7;
            datcomp.Data = value.Item5;
        }
    }
}
