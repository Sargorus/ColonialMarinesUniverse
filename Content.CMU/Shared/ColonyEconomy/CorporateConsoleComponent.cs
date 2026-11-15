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
    ///     Chemistry science points charged for a WEYU PMC squad call.
    /// </summary>
    [DataField("pmcCostChemistry")]
    public int PmcCostChemistry = 25;

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
}
