using Content.Server.CMU14.Chat;
using Content.Server.CMU14.Ops.ThirdParty;
using Content.Server.CMU14.Round;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Threats;
using Content.Shared.CMU14.Threats.Mobs.Wendigo.Lab;
using Content.Shared.CMU14.Yautja;
using Content.Shared.GameTicking;
using Content.Shared.Stacks;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using ThirdPartySystem = Content.Server.CMU14.Ops.ThirdParty.ThirdPartySystem;

namespace Content.Server.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleSystem : EntitySystem
{
    private static readonly EntProtoId CashPrototype = "RMCSpaceCash";

    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ThirdPartySystem _thirdParty = default!;
    [Dependency] private AuRoundSystem _auRound = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private AdminConsoleSystem _adminConsole = default!;
    [Dependency] private ColonyBudgetSystem _colonyBudget = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;

    private static readonly ProtoId<TagPrototype> CurrencyTag = "Currency";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CorporateConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CorporateConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleSetTariffBuiMsg>(OnSetTariff);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleCallThirdPartyBuiMsg>(OnCallThirdParty);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleWithdrawBuiMsg>(OnWithdraw);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleOpenThirdPartyBuiMsg>(OnOpenThirdParty);
        SubscribeLocalEvent<CorporateConsoleComponent, EntInsertedIntoContainerMessage>(OnCashInserted);
        SubscribeLocalEvent<CorporateConsoleComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleAcceptMissionBuiMsg>(OnAcceptMission);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleSetDirectionBuiMsg>(OnSetDirection);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleCallPmcBuiMsg>(OnCallPmc);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleCallGuardBuiMsg>(OnCallGuard);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleRequestCreditBuiMsg>(OnRequestCredit);
        SubscribeLocalEvent<CorporateConsoleComponent, CorporateConsoleRequestAlienTechBuiMsg>(OnRequestAlienTech);
        SubscribeLocalEvent<CMUChemicalScannedEvent>(OnChemicalScanned);
        SubscribeLocalEvent<CMUWendigoCreatedEvent>(OnWendigoCreated);
        SubscribeLocalEvent<CMUYautjaExperimentComponent, CMUYautjaBracerUnlockedEvent>(OnBracerUnlocked);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        ResetMissionState();
    }

    /// <summary>
    ///     When a new corporate console spawns, copy tariff and budget from any existing
    ///     corporate console so all terminals start in sync.
    /// </summary>
    private void OnMapInit(EntityUid uid, CorporateConsoleComponent comp, MapInitEvent args)
    {
        var query = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (query.MoveNext(out var otherUid, out var other))
        {
            if (otherUid == uid)
                continue;

            comp.TransitTariffPercent = other.TransitTariffPercent;
            comp.CorporateBudget = other.CorporateBudget;
            comp.PmcCalls = other.PmcCalls;
            comp.NextPmcCallAt = other.NextPmcCallAt;
            comp.GuardCalls = other.GuardCalls;
            comp.NextGuardCallAt = other.NextGuardCallAt;
            comp.NextCreditAt = other.NextCreditAt;
            comp.SupportStatus = other.SupportStatus;
            return;
        }
    }

    // ── Public query / mutation API ──────────────────────────────────────────

    /// <summary>
    ///     Returns the current transit tariff as a fraction (e.g. 0.15 = 15%).
    ///     Reads from the first CorporateConsoleComponent found.
    /// </summary>
    public float GetTariff()
    {
        var q = EntityQueryEnumerator<CorporateConsoleComponent>();
        return q.MoveNext(out _, out var comp) ? comp.TransitTariffPercent / 100f : 0f;
    }

    /// <summary>
    ///     Adds <paramref name="amount"/> to the corporate budget on every
    ///     CorporateConsoleComponent so all terminals stay in sync.
    /// </summary>
    public void AddToCorporateBudget(float amount)
    {
        var q = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (q.MoveNext(out _, out var comp))
            comp.CorporateBudget += amount;

        UpdateAllUi();
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnUiOpened(EntityUid uid, CorporateConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, comp);
    }

    private void OnOpenThirdParty(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleOpenThirdPartyBuiMsg msg)
    {
        _ui.TryToggleUi(uid, CorporateConsoleThirdPartyUi.Key, msg.Actor);
    }

    private void OnSetTariff(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleSetTariffBuiMsg msg)
    {
        var clamped = Math.Clamp(msg.TariffPercent, 0f, 50f);
        var oldTariff = comp.TransitTariffPercent;

        // Sync tariff to ALL corporate consoles
        var query = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (query.MoveNext(out _, out var c))
            c.TransitTariffPercent = clamped;

        if (Math.Abs(oldTariff - clamped) > 0.01f)
        {
            var sound = new Robust.Shared.Audio.SoundPathSpecifier("/Audio/Announcements/announce.ogg");
            _chat.DispatchFilteredAnnouncement(
                ColonyAnnouncements.Recipients(EntityManager),
                Loc.GetString("corporate-console-tariff-announcement", ("percent", $"{clamped:F0}")),
                sender: Loc.GetString("corporate-console-announcement-sender"),
                playSound: true,
                announcementSound: sound);
        }

        UpdateAllUi();
    }

    private void OnCallThirdParty(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleCallThirdPartyBuiMsg msg)
    {
        if (!comp.CallableParties.TryGetValue(msg.ThirdPartyId, out var cost))
            return;
        if (comp.CalledParties.Contains(msg.ThirdPartyId))
            return;
        if (comp.CalledParties.Count >= comp.MaxCallIns || _timing.CurTime < comp.NextCallInAt)
            return;
        if (comp.CorporateBudget < cost)
            return;
        if (!_proto.TryIndex<ThirdPartyPrototype>(msg.ThirdPartyId, out var partyProto))
            return;
        if (!_auRound.IsThirdPartyAllowedForCurrentContext(partyProto))
            return;
        if (!_proto.TryIndex(partyProto.PartySpawn, out var spawnProto))
            return;

        _thirdParty.SpawnThirdParty(partyProto, spawnProto, false,
            caller: new ThirdPartyCaller(Name(msg.Actor), Loc.GetString("cmu-third-party-caller-corporate"), Actor: msg.Actor, Console: uid));

        // Deduct from ALL corporate consoles (they share one budget)
        var nextCallIn = _timing.CurTime + comp.CallInCooldown;
        var q = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (q.MoveNext(out _, out var c))
        {
            c.CorporateBudget -= cost;
            c.CalledParties.Add(msg.ThirdPartyId);
            c.NextCallInAt = nextCallIn;
        }

        UpdateAllUi();
    }

    private void OnWithdraw(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleWithdrawBuiMsg msg)
    {
        if (msg.Amount <= 0 || msg.Amount > comp.CorporateBudget)
            return;

        // Deduct from ALL corporate consoles
        var q = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (q.MoveNext(out _, out var c))
            c.CorporateBudget -= msg.Amount;

        // Apply income tax — tax goes to colony budget, remainder dispensed as cash
        var incomeTax = _adminConsole.GetIncomeTax();
        var taxAmount = (int) Math.Floor(msg.Amount * incomeTax);
        var netAmount = (int) msg.Amount - taxAmount;

        if (netAmount > 0)
            _stack.SpawnMultipleNextToOrDrop(CashPrototype, netAmount, uid);
        if (taxAmount > 0)
            _colonyBudget.AddToBudget(taxAmount);

        UpdateAllUi();
    }

    private void OnCashInserted(EntityUid uid, CorporateConsoleComponent comp, EntInsertedIntoContainerMessage args)
    {
        var stackCount = 1;
        if (TryComp<StackComponent>(args.Entity, out var stack))
            stackCount = stack.Count;

        // Add to ALL corporate consoles (shared budget)
        var q = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (q.MoveNext(out _, out var c))
            c.CorporateBudget += stackCount;

        QueueDel(args.Entity);
        UpdateAllUi();
    }

    /// <summary>
    ///     Fallback: if ItemSlots didn't catch the cash, handle it directly.
    /// </summary>
    private void OnInteractUsing(EntityUid uid, CorporateConsoleComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_tag.HasTag(args.Used, CurrencyTag))
            return;

        args.Handled = true;

        var stackCount = 1;
        if (TryComp<StackComponent>(args.Used, out var stack))
            stackCount = stack.Count;

        var q = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (q.MoveNext(out _, out var c))
            c.CorporateBudget += stackCount;

        QueueDel(args.Used);
        UpdateAllUi();
    }

    // ── UI helpers ───────────────────────────────────────────────────────────

    private void UpdateUiState(EntityUid uid, CorporateConsoleComponent comp)
    {
        _ui.SetUiState(uid, CorporateConsoleUi.Key, BuildUiState(uid, comp));

        var thirdParties = new Dictionary<string, (string DisplayName, float Cost)>();
        foreach (var (id, cost) in comp.CallableParties)
        {
            if (_proto.TryIndex<ThirdPartyPrototype>(id, out var proto) &&
                _auRound.IsThirdPartyAllowedForCurrentContext(proto))
                thirdParties[id] = (proto.DisplayName ?? proto.ID, cost);
        }
        _ui.SetUiState(uid, CorporateConsoleThirdPartyUi.Key,
            new CorporateConsoleThirdPartyBuiState(comp.CorporateBudget, thirdParties, comp.CalledParties)
            {
                CallsLeft = Math.Max(0, comp.MaxCallIns - comp.CalledParties.Count),
                NextCallInAt = comp.NextCallInAt,
            });
    }

    /// <summary>
    /// Builds the main console screen, including the science balances, company mission board, PMC state and
    /// the complex's chosen research direction.
    /// </summary>
    private CorporateConsoleBuiState BuildUiState(EntityUid uid, CorporateConsoleComponent comp)
    {
        var faction = GetConsoleFaction(uid);
        var science = QueryScience(faction);
        var state = GetMissionState(faction);
        return new CorporateConsoleBuiState(
            comp.TransitTariffPercent,
            comp.CorporateBudget,
            _adminConsole.BuildEconomyStatus(),
            science.ChemistryPoints,
            science.RoboticsPoints,
            state.CompanyValue,
            science.ScienceLevel,
            state.Direction,
            BuildAvailableMissions(state),
            BuildActiveMissions(state),
            BuildMissionHistory(state),
            comp.PmcCostCompanyValue,
            Math.Max(0, comp.MaxPmcCalls - comp.PmcCalls),
            comp.NextPmcCallAt,
            Math.Max(0, (int) Math.Ceiling((comp.NextPmcCallAt - _timing.CurTime).TotalSeconds)),
            comp.PmcCalls > 0,
            comp.CreditCostCompanyValue,
            comp.CreditAmount,
            comp.AlienTechCostCompanyValue,
            comp.GuardCostCompanyValue,
            Math.Max(0, comp.MaxGuardCalls - comp.GuardCalls),
            Math.Max(0, (int) Math.Ceiling((comp.NextGuardCallAt - _timing.CurTime).TotalSeconds)),
            comp.GuardCalls > 0,
            state.Status,
            comp.SupportStatus);
    }

    private void UpdateAllUi()
    {
        var q = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (q.MoveNext(out var uid, out var comp))
            UpdateUiState(uid, comp);
    }
}
