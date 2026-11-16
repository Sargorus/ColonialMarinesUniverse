using Content.Server.Administration.Logs;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Server.CMU14.ColonyEconomy;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared.CCVar;
using Content.Shared.CMU14.Threats;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Discord.Rest;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDataTerminalSystem : SharedResearchDataTerminalSystem
{
    public sealed class FactionResearch
    {
        public List<GeneratedReagentData> Selectable = [];
        public HashSet<string> CompletedChemicals = [];
        public TimeSpan NextReroll;
        public TimeSpan LastTime;
        public bool Picked;
        public string LastPickName = string.Empty;
        public string LastPick = string.Empty;
        public Dictionary<int, (string, string, TimeSpan, bool, GeneratedReagentData, bool, bool)> Reports = [];
        /// <summary>
        /// ARCH-03: every contract this faction has picked this round, in pick order, including new nested
        /// sub-contracts. Shown on the terminal's Contracts tab.
        /// </summary>
        public List<string> PickedContracts = [];

        /// <summary>Property that must lead the next generated contract offer.</summary>
        public string? GuaranteedProperty;
        public int GuaranteedPropertyCost;
    }

    /// <summary>ARCH-02: chance that a picked Hard contract nests another contract chemical as a reactant.</summary>
    public const float NestedContractChance = 0.30f;

    /// <summary>ARCH-02: chance that a nested chemical reuses one of the faction's completed contracts, when any exist.</summary>
    public const float ReuseCompletedSubContractChance = 0.5f;

    /// <summary>ARCH-02: recipe rolls for the parent contract before nesting is abandoned for a plain recipe.</summary>
    private const int NestedRecipeAttempts = 3;

    /// <summary>FLOW-02: sample data for completing an Easy contract chemical.</summary>
    public const int ContractSampleData = 1;

    /// <summary>FLOW-02: sample data for completing an Intermediate contract chemical.</summary>
    public const int IntermediateContractSampleData = 2;

    /// <summary>FLOW-02: sample data for completing a Hard contract chemical.</summary>
    public const int HardContractSampleData = 4;

    /// <summary>FLOW-24: the GOVFOR research faction id, locked out of research while Insurgency runs.</summary>
    public const string GovforFaction = "govfor";

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private Content.Server.CMU14.Threats.Mobs.Wendigo.Lab.CMUWendigoResearchUnlockSystem _wendigoUnlock = default!;

    private readonly Dictionary<string, FactionResearch> _factions = new(StringComparer.OrdinalIgnoreCase);
    public FactionResearch GetResearch(string faction)
    {
        if (!_factions.TryGetValue(faction, out var research))
            _factions[faction] = research = new FactionResearch();
        return research;
    }

    public List<GeneratedReagentData> Selectable => GetResearch("corporate").Selectable;
    public TimeSpan NextReroll => GetResearch("corporate").NextReroll;
    public Dictionary<int, (string, string, TimeSpan, bool, GeneratedReagentData, bool, bool)> ResearchData => GetResearch("corporate").Reports;
    public TimeSpan RerollTime = TimeSpan.FromSeconds(180);
    public TimeSpan PickedRerollTime = TimeSpan.FromSeconds(360);
    public int ResearchChemAmount = 6;
    public float ResearchCashRewardMult = 500;
    private bool ready;
    private int _nextContractId;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan XClearanceLockout = TimeSpan.FromMinutes(40);

    [Dependency] private ServerReagentGeneratorSystem _generator = default!;
    [Dependency] private IGameTiming _timer = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private IPrototypeManager _protoman = default!;
    [Dependency] private MetaDataSystem _mets = default!;
    [Dependency] private CorporateConsoleSystem _corpo = default!;
    [Dependency] private ColonyBudgetSystem _colbud = default!;
    [Dependency] private SharedRequisitionsSystem _reqsys = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILogManager _logman = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private XRFScannerSystem _scanner = default!;
    [Dependency] private SharedGameTicker _ticker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private Dictionary<Entity<ResearchDataTerminalComponent>, int> _printing = [];
    private HashSet<Entity<ResearchDataTerminalComponent>> _printingLast = [];
    private List<ResearchGuaranteeEntry>? _guaranteeEntries;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logman.GetSawmill("reagent");

        SubscribeLocalEvent<PostGameMapLoad>(OnLoadingMaps);
        SubscribeLocalEvent<ResearchDataTerminalComponent, BoundUIOpenedEvent>(OnUiOpen);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        Subs.BuiEvents<ResearchDataTerminalComponent>(ResearchDataTerminalUI.Key, subs =>
        {
            subs.Event<ResearchDataTerminalAttemptUpgradeBuiMsg>(OnUpgradeAttempt);
            subs.Event<CMUResearchReduceCooldownBuiMsg>(OnReduceCooldown);
            subs.Event<ResearchDataTerminalPickChemBuiMsg>(OnPickChem);
            subs.Event<ResearchDataTerminalPrintLastBuiMsg>(OnPrintLast);
            subs.Event<ResearchDataTerminalPrintChemBuiMsg>(OnPrintRequest);
            subs.Event<ResearchDataTerminalBuyGuaranteeBuiMsg>(OnBuyGuarantee);
        });
        Subs.CVar(_cfg, CCVars.PickWaitTime, time => PickedRerollTime = TimeSpan.FromSeconds(time), true);
        Subs.CVar(_cfg, CCVars.RefreshTime, time => RerollTime = TimeSpan.FromSeconds(time), true);
        Subs.CVar(_cfg, CCVars.TerminalChems, chems => ResearchChemAmount = chems, true);
        Subs.CVar(_cfg, CCVars.CashRewardMult, dosh => ResearchCashRewardMult = dosh, true);
        Subs.CVar(_cfg, CCVars.XClearanceLockout, t => XClearanceLockout = TimeSpan.FromSeconds(t), true);
    }

    /// <summary>
    /// FLOW-24: GOVFOR may not research during Insurgency. True for the govfor faction while a game rule with
    /// <see cref="InsurgencyRuleComponent"/> is active; every other faction, and govfor in other modes, is unaffected.
    /// </summary>
    public bool IsResearchBlocked(string faction)
    {
        if (!string.Equals(faction, GovforFaction, StringComparison.OrdinalIgnoreCase))
            return false;
        var query = EntityQueryEnumerator<InsurgencyRuleComponent, ActiveGameRuleComponent, GameRuleComponent>();
        return query.MoveNext(out _, out _, out _, out _);
    }

    /// <summary>FLOW-24: tells the user the request was refused because the faction's research is locked.</summary>
    private void PopupBlocked(EntityUid terminal, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString("research-locked-popup"), terminal, user, PopupType.SmallCaution);
    }

    protected override void OnResearchBalanceChanged(string faction) => UpdateFactionUI(faction);

    protected override void OnClearanceChanged(string faction, int oldClearance, int newClearance) =>
        _wendigoUnlock.OnClearanceChanged(faction, oldClearance, newClearance);

    private void UpdateFactionUI(string faction, bool announce = false)
    {
        var query = EntityQueryEnumerator<ResearchDataTerminalComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!string.Equals(comp.Faction, faction, StringComparison.OrdinalIgnoreCase))
                continue;
            if (announce)
                _chat.TrySendInGameICMessage(uid, Loc.GetString("research-chem-terminal-update"),
                    InGameICChatType.Speak, false, ignoreActionBlocker: true);
            UpdateUI((uid, comp));
        }
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        ready = false;
        DDIDiscovered = false;
        _factions.Clear();
        _nextContractId = 0;
        ResetResearchAccounts();
        _printing.Clear();
        _printingLast.Clear();
    }

    public void OnLoadingMaps(PostGameMapLoad args)
    {
        ready = true;
    }

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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var printee in _printingLast)
            PrintLast(printee);
        _printingLast.Clear();
        foreach (var printee in _printing)
            PrintData(printee.Key, printee.Value);
        _printing.Clear();
        if (!ready)
            return;
        // Initialize accounts even when their terminals are delivered later in the round.
        foreach (var faction in new[] { "corporate", "govfor", "opfor", "colony" })
            GetResearch(faction);
        foreach (var (faction, research) in _factions.ToArray())
        {
            if (_timer.CurTime >= research.NextReroll)
                RerollChems(faction);
        }
    }

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
        if (faction is not null && chem.GenTier >= 3 &&
            (forceNested ?? _random.Prob(NestedContractChance)) &&
            TryLegalizeNested(ref chem, faction, reuseCompleted))
        {
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
            guaranteedPropertyCost: research.GuaranteedPropertyCost);
        _ui.SetUiState(ent.Owner, ResearchDataTerminalUI.Key, state);
    }

    /// <summary>
    /// Atomically reserves one contract for a faction before contract generation raises further events.
    /// </summary>
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

    private bool TryGetCipherElevator(EntityUid terminal, EntityUid actor, out Entity<RequisitionsElevatorComponent> elevator)
    {
        if (TryComp<MarineComponent>(actor, out var marine)
            && !string.IsNullOrEmpty(marine.Faction))
        {
            var actorQuery = EntityQueryEnumerator<RequisitionsElevatorComponent>();
            while (actorQuery.MoveNext(out var elevUid, out var elevComp))
            {
                if (elevComp.Faction.Equals(marine.Faction, StringComparison.OrdinalIgnoreCase))
                {
                    elevator = (elevUid, elevComp);
                    return true;
                }
            }
        }

        var xrf = GetNearestXRF(terminal);
        if (xrf != EntityUid.Invalid)
        {
            var net = _scanner.GetFactionElevator(xrf, null);
            if (net != NetEntity.Invalid
                && TryComp<RequisitionsElevatorComponent>(GetEntity(net), out var xrfComp))
            {
                elevator = (GetEntity(net), xrfComp);
                return true;
            }
        }

        elevator = default;
        return false;
    }

    public EntityUid GetNearestXRF(EntityUid ent)
    {
        if (!TryComp(ent, out TransformComponent? excomp))
            return EntityUid.Invalid;
        var scannersq = EntityQueryEnumerator<XRFScannerComponent>();
        List<(EntityUid, TransformComponent)> scanners = [];
        EntityUid closest = EntityUid.Invalid;
        float closestDistance = float.MaxValue;
        while (scannersq.MoveNext(out var xent, out var scanner))
        {
            if (!string.Equals(scanner.Faction, GetFaction(ent), StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TryComp(xent, out TransformComponent? xcomp))
                continue;
            if (excomp.MapUid != xcomp.MapUid)
                continue;
            float dist = System.Numerics.Vector2.Distance(_xform.GetWorldPosition(ent), _xform.GetWorldPosition(xent));
            if (closestDistance > dist)
            {
                closestDistance = dist;
                closest = xent;
            }
        }
        return closest;
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
