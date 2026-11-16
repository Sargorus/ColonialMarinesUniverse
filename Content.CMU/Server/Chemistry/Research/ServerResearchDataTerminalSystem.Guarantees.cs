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

// Research-credit purchases that force a property into a contract offer of the matching tier.
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
        if (GetCredits(faction) < cost)
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
        UpdateClearance(GetCredits(faction) - cost, -1, faction, GetSampleData(faction));
        research.GuaranteedProperty = property.ID;
        research.GuaranteedPropertyCost = cost;
        if (!research.Picked)
            ApplyGuaranteeToOffers(faction, research);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor)} guaranteed chemistry property {property.ID} for {faction} " +
            $"for {cost} credits");
        UpdateFactionUI(faction);
    }

    /// <summary>
    /// Puts the guaranteed property into an existing offer of an allowed tier. If the pool has no such
    /// offer, one is generated so a legendary property always has a hard contract to land on.
    /// </summary>
    private void ApplyGuaranteeToOffers(string faction, FactionResearch research)
    {
        if (research.GuaranteedProperty is not { } propertyId)
            return;
        if (!_protoman.TryIndex<ReagentPropertyPrototype>(propertyId, out var property))
        {
            ClearGuarantee(research);
            return;
        }

        var index = research.Selectable.FindIndex(offer => IsGuaranteeTierAllowed(property, offer.GenTier));
        if (index < 0)
        {
            if (research.Selectable.Count == 0)
                return;

            // Replace an offer rather than growing the pool: the player explicitly paid for this tier.
            research.Selectable[0] = GenerateContractOffer(faction, GetGuaranteeTier(property));
            index = 0;
        }

        var offer = research.Selectable[index];
        ForceFirstProperty(ref offer, propertyId);
        research.Selectable[index] = offer;
        ClearGuarantee(research);
    }

    private static void ClearGuarantee(FactionResearch research)
    {
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
                GetGuaranteeCost(property),
                GetGuaranteeRequirement(property)));
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

    /// <summary>
    /// Contract tier a guaranteed property must land on. Easy is common/uncommon only, rare properties
    /// need at least an intermediate contract, and legendary properties require a hard contract.
    /// </summary>
    private static int GetGuaranteeTier(ReagentPropertyPrototype property)
    {
        return property.Rarity switch
        {
            ReagentPropertyRarityEnum.Legendary => 3,
            ReagentPropertyRarityEnum.Rare => 2,
            _ => 1,
        };
    }

    private static bool IsGuaranteeTierAllowed(ReagentPropertyPrototype property, int tier)
    {
        return property.Rarity switch
        {
            ReagentPropertyRarityEnum.Legendary => tier == 3,
            ReagentPropertyRarityEnum.Rare => tier >= 2,
            _ => tier == 1,
        };
    }

    private string GetGuaranteeRequirement(ReagentPropertyPrototype property)
    {
        return property.Rarity switch
        {
            ReagentPropertyRarityEnum.Legendary => Loc.GetString("research-data-ui-guarantee-requirement-hard"),
            ReagentPropertyRarityEnum.Rare => Loc.GetString("research-data-ui-guarantee-requirement-medium"),
            _ => Loc.GetString("research-data-ui-guarantee-requirement-easy"),
        };
    }

    private static int GetGuaranteeCost(ReagentPropertyPrototype property)
    {
        return property.Rarity switch
        {
            ReagentPropertyRarityEnum.Uncommon => 10,
            ReagentPropertyRarityEnum.Legendary => 15,
            _ => 8,
        };
    }
}
