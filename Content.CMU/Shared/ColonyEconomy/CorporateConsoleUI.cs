using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.ColonyEconomy;

[Serializable, NetSerializable]
public enum CorporateConsoleUi
{
    Key,
}

[Serializable, NetSerializable]
public enum CorporateConsoleThirdPartyUi
{
    Key,
}

// ── BUI States ──────────────────────────────────────────────────────────────

[Serializable, NetSerializable]
public sealed class CorporateConsoleBuiState : BoundUserInterfaceState
{
    /// <summary>Current transit tariff in percent (0–50).</summary>
    public float TransitTariffPercent { get; }

    /// <summary>Current corporate budget balance.</summary>
    public float CorporateBudget { get; }

    public EconomyStatusState EconomyStatus { get; }

    /// <summary>Chemistry science points (sample data) held by the corporate research account.</summary>
    public int ChemistryPoints { get; }

    /// <summary>Robotics science points held by the corporate research account.</summary>
    public int RoboticsPoints { get; }

    /// <summary>The liaison's third currency: how valuable the complex is to the company.</summary>
    public int CompanyValue { get; }

    /// <summary>Research clearance level shown on the science tab.</summary>
    public int ScienceLevel { get; }

    public CorporateResearchDirection Direction { get; }

    public List<CompanyMissionInfo> AvailableMissions { get; }
    public List<CompanyMissionInfo> ActiveMissions { get; }
    public List<CompanyMissionInfo> MissionHistory { get; }

    public int PmcCostCompanyValue { get; }
    public int PmcCallsLeft { get; }
    public TimeSpan PmcNextCallInAt { get; }
    public int PmcCooldownSeconds { get; }
    public bool PmcCalled { get; }

    public int CreditCostCompanyValue { get; }
    public int CreditAmount { get; }
    public int AlienTechCostCompanyValue { get; }

    /// <summary>Last mission action result shown on the Company Tasks tab.</summary>
    public string MissionStatus { get; }

    /// <summary>Last Company Support action result shown on the support tab.</summary>
    public string SupportStatus { get; }

    public CorporateConsoleBuiState(
        float transitTariffPercent,
        float corporateBudget,
        EconomyStatusState economyStatus,
        int chemistryPoints = 0,
        int roboticsPoints = 0,
        int companyValue = 0,
        int scienceLevel = 1,
        CorporateResearchDirection direction = CorporateResearchDirection.None,
        List<CompanyMissionInfo>? availableMissions = null,
        List<CompanyMissionInfo>? activeMissions = null,
        List<CompanyMissionInfo>? missionHistory = null,
        int pmcCostCompanyValue = 0,
        int pmcCallsLeft = 0,
        TimeSpan pmcNextCallInAt = default,
        int pmcCooldownSeconds = 0,
        bool pmcCalled = false,
        int creditCostCompanyValue = 0,
        int creditAmount = 0,
        int alienTechCostCompanyValue = 0,
        string missionStatus = "",
        string supportStatus = "")
    {
        TransitTariffPercent = transitTariffPercent;
        CorporateBudget = corporateBudget;
        EconomyStatus = economyStatus;
        ChemistryPoints = chemistryPoints;
        RoboticsPoints = roboticsPoints;
        CompanyValue = companyValue;
        ScienceLevel = scienceLevel;
        Direction = direction;
        AvailableMissions = availableMissions ?? new List<CompanyMissionInfo>();
        ActiveMissions = activeMissions ?? new List<CompanyMissionInfo>();
        MissionHistory = missionHistory ?? new List<CompanyMissionInfo>();
        PmcCostCompanyValue = pmcCostCompanyValue;
        PmcCallsLeft = pmcCallsLeft;
        PmcNextCallInAt = pmcNextCallInAt;
        PmcCooldownSeconds = pmcCooldownSeconds;
        PmcCalled = pmcCalled;
        CreditCostCompanyValue = creditCostCompanyValue;
        CreditAmount = creditAmount;
        AlienTechCostCompanyValue = alienTechCostCompanyValue;
        MissionStatus = missionStatus;
        SupportStatus = supportStatus;
    }
}

[Serializable, NetSerializable]
public sealed class CorporateConsoleThirdPartyBuiState : BoundUserInterfaceState
{
    public float Budget { get; }
    public Dictionary<string, (string DisplayName, float Cost)> CallableParties { get; }
    public HashSet<string> CalledParties { get; }

    /// <summary>How many more third parties can be called in this round.</summary>
    public int CallsLeft { get; init; }

    /// <summary>When the next one can be called in.</summary>
    public TimeSpan NextCallInAt { get; init; }

    public CorporateConsoleThirdPartyBuiState(float budget, Dictionary<string, (string DisplayName, float Cost)> callableParties, HashSet<string> calledParties)
    {
        Budget = budget;
        CallableParties = callableParties;
        CalledParties = calledParties;
    }
}

// ── BUI Messages ─────────────────────────────────────────────────────────────

[Serializable, NetSerializable]
public sealed class CorporateConsoleSetTariffBuiMsg : BoundUserInterfaceMessage
{
    /// <summary>New transit tariff in percent (0–50).</summary>
    public float TariffPercent { get; }
    public CorporateConsoleSetTariffBuiMsg(float tariffPercent) { TariffPercent = tariffPercent; }
}

[Serializable, NetSerializable]
public sealed class CorporateConsoleOpenThirdPartyBuiMsg : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CorporateConsoleCallThirdPartyBuiMsg : BoundUserInterfaceMessage
{
    public string ThirdPartyId { get; }
    public CorporateConsoleCallThirdPartyBuiMsg(string thirdPartyId) { ThirdPartyId = thirdPartyId; }
}

[Serializable, NetSerializable]
public sealed class CorporateConsoleWithdrawBuiMsg : BoundUserInterfaceMessage
{
    public float Amount { get; }
    public CorporateConsoleWithdrawBuiMsg(float amount) { Amount = amount; }
}

