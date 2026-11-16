using Content.Server.Administration.Logs;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Server.CMU14.ColonyEconomy;
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
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDataTerminalSystem : SharedResearchDataTerminalSystem
{
    public sealed class FactionResearch
    {
        public List<GeneratedReagentData> Selectable = [];
        public HashSet<string> CompletedChemicals = [];
        /// <summary>Facts already accepted at this terminal; also prevents duplicate payouts.</summary>
        public HashSet<string> KnownFacts = [];
        /// <summary>Chemicals whose one-time credit/sample/cash reward has already been paid.</summary>
        public HashSet<string> RewardedChemicals = [];
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
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILogManager _logman = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private XRFScannerSystem _scanner = default!;
    [Dependency] private SharedGameTicker _ticker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private Dictionary<Entity<ResearchDataTerminalComponent>, int> _printing = [];
    private HashSet<Entity<ResearchDataTerminalComponent>> _printingLast = [];

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
}
