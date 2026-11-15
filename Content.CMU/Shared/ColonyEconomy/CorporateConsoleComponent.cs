namespace Content.Shared.CMU14.ColonyEconomy;

/// <summary>
///     Corporate console: manages transit tariffs and can call third parties.
///     Third-party costs are paid from the corporate budget.
///     TransitTariffPercent and CorporateBudget are stored on the component;
///     CorporateConsoleSystem keeps all consoles in sync.
/// </summary>
[RegisterComponent]
public sealed partial class CorporateConsoleComponent : Component
{
    /// <summary>
    ///     Current transit tariff in percent (0–50).
    ///     This portion of every submission storage payout goes to the corporate budget.
    /// </summary>
    [DataField("transitTariffPercent")]
    public float TransitTariffPercent = 0f;

    /// <summary>
    ///     The corporate department's cash balance.
    ///     Fed by transit tariffs; spent on third-party calls or cash withdrawals.
    /// </summary>
    public float CorporateBudget = 0f;

    /// <summary>
    ///     Map of third-party prototype ID → cost deducted from corporate budget.
    /// </summary>
    [DataField("callableParties")]
    public Dictionary<string, float> CallableParties = new();

    public HashSet<string> CalledParties = new();

    /// <summary>
    ///     How many third parties can be called in per round, across every console of this kind.
    /// </summary>
    [DataField("maxCallIns")]
    public int MaxCallIns = 3;

    /// <summary>
    ///     How long after one call-in before the next can be made.
    /// </summary>
    [DataField("callInCooldown")]
    public TimeSpan CallInCooldown = TimeSpan.FromMinutes(20);

    /// <summary>
    ///     When the next call-in can be made. Shared by every console of this kind.
    /// </summary>
    public TimeSpan NextCallInAt;

    /// <summary>
    ///     Company value required to call a WEYU PMC squad. The requirement is not spent.
    /// </summary>
    [DataField("pmcCostCompanyValue")]
    public int PmcCostCompanyValue = 30;

    /// <summary>
    ///     How many PMC squads can be called per round, shared by every corporate console.
    /// </summary>
    [DataField("maxPmcCalls")]
    public int MaxPmcCalls = 1;

    /// <summary>
    ///     PMC squads already called this round.
    /// </summary>
    public int PmcCalls;

    /// <summary>
    ///     Earliest time another PMC squad may be called.
    /// </summary>
    public TimeSpan NextPmcCallAt;

    /// <summary>
    ///     Delay between PMC squad calls, shared by every corporate console.
    /// </summary>
    [DataField("pmcCooldown")]
    public TimeSpan PmcCooldown = TimeSpan.FromMinutes(20);

    /// <summary>
    ///     Company value charged for a corporate credit request.
    /// </summary>
    [DataField("creditCostCompanyValue")]
    public int CreditCostCompanyValue = 10;

    /// <summary>
    ///     Corporate budget credited by one credit request.
    /// </summary>
    [DataField("creditAmount")]
    public int CreditAmount = 10000;

    /// <summary>
    ///     Earliest time another credit request may be made.
    /// </summary>
    public TimeSpan NextCreditAt;

    /// <summary>
    ///     Delay between credit requests.
    /// </summary>
    [DataField("creditCooldown")]
    public TimeSpan CreditCooldown = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Company value charged for a Yautja technology delivery.
    /// </summary>
    [DataField("alienTechCostCompanyValue")]
    public int AlienTechCostCompanyValue = 20;

    /// <summary>
    ///     Company value charged for an additional corporate guard squad.
    /// </summary>
    [DataField("guardCostCompanyValue")]
    public int GuardCostCompanyValue = 20;

    /// <summary>
    ///     How many guard squads can be called per round, shared by every corporate console.
    /// </summary>
    [DataField("maxGuardCalls")]
    public int MaxGuardCalls = 1;

    /// <summary>
    ///     Guard squads already called this round.
    /// </summary>
    public int GuardCalls;

    /// <summary>
    ///     Earliest time another guard squad may be called.
    /// </summary>
    public TimeSpan NextGuardCallAt;

    /// <summary>
    ///     Delay between guard squad calls.
    /// </summary>
    [DataField("guardCooldown")]
    public TimeSpan GuardCooldown = TimeSpan.FromMinutes(20);

    /// <summary>
    ///     Last Company Support action result.
    /// </summary>
    public string SupportStatus = string.Empty;
}
