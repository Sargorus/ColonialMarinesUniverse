using System;
using System.Collections.Generic;
using Content.Server.Administration.Logs;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

// Chemistry-point purchases that force a property into the next contract offer.
public sealed partial class ServerResearchDataTerminalSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;

    private List<ResearchGuaranteeEntry>? _guaranteeEntries;

    private void OnBuyGuarantee(
        Entity<ResearchDataTerminalComponent> ent,
        ref ResearchDataTerminalBuyGuaranteeBuiMsg args)
    {
        var faction = ent.Comp.Faction;
        if (IsResearchBlocked(faction))
        {
            PopupBlocked(ent.Owner, args.Actor);
            UpdateUI(ent);
            return;
        }

        if (!_protoman.TryIndex<ReagentPropertyPrototype>(args.PropertyId, out var property)
            || !IsGuaranteeProperty(property))
        {
            return;
        }

        var cost = GetGuaranteeCost(property);
        if (GetSampleData(faction) < cost)
        {
            _popup.PopupEntity(
                Loc.GetString("research-data-ui-guarantee-no-points"),
                ent.Owner,
                args.Actor,
                PopupType.SmallCaution);
            UpdateUI(ent);
            return;
        }

        var research = GetResearch(faction);
        UpdateSampleData(GetSampleData(faction) - cost, faction);
        research.GuaranteedProperty = property.ID;
        research.GuaranteedPropertyCost = cost;
        if (!research.Picked)
            ApplyGuaranteeToFirstOffer(research);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor)} guaranteed chemistry property {property.ID} for {faction} " +
            $"for {cost} points");
        UpdateFactionUI(faction);
    }

    private void ApplyGuaranteeToFirstOffer(FactionResearch research)
    {
        if (research.GuaranteedProperty is not { } property || research.Selectable.Count == 0)
            return;

        var first = research.Selectable[0];
        ForceFirstProperty(ref first, property);
        research.Selectable[0] = first;
        research.GuaranteedProperty = null;
        research.GuaranteedPropertyCost = 0;
    }

    private static void ForceFirstProperty(ref GeneratedReagentData data, string property)
    {
        var level = data.Effects.TryGetValue(property, out var existing)
            ? Math.Max(1, existing)
            : 1;
        data.Effects.Remove(property);

        var reordered = new Dictionary<string, int>(data.Effects.Count + 1)
        {
            [property] = level,
        };
        foreach (var (id, propertyLevel) in data.Effects)
            reordered[id] = propertyLevel;

        data.Effects = reordered;
        data.PropertyHint = property;
    }

    private List<ResearchGuaranteeEntry> GetGuaranteeEntries()
    {
        if (_guaranteeEntries is not null)
            return _guaranteeEntries;

        _guaranteeEntries = [];
        foreach (var property in _protoman.GetInstances<ReagentPropertyPrototype>().Values)
        {
            if (!IsGuaranteeProperty(property))
                continue;

            _guaranteeEntries.Add(new ResearchGuaranteeEntry(
                property.ID,
                property.LocalizedName,
                GetGuaranteeCost(property)));
        }

        _guaranteeEntries.Sort((a, b) =>
        {
            var cost = a.Cost.CompareTo(b.Cost);
            return cost != 0 ? cost : string.CompareOrdinal(a.Name, b.Name);
        });
        return _guaranteeEntries;
    }

    private static bool IsGuaranteeProperty(ReagentPropertyPrototype property)
    {
        return !property.Abstract
               && !string.IsNullOrEmpty(property.EffectName)
               && property.Rarity is not (ReagentPropertyRarityEnum.Admin
                   or ReagentPropertyRarityEnum.Disabled);
    }

    private static int GetGuaranteeCost(ReagentPropertyPrototype property)
    {
        return property.Rarity switch
        {
            ReagentPropertyRarityEnum.Uncommon => 5,
            ReagentPropertyRarityEnum.Legendary => 10,
            _ => 3,
        };
    }
}
