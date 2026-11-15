namespace Content.Server.CMU14.ColonyEconomy;

// Company mission definitions and balance values.
public sealed partial class CorporateConsoleSystem
{
    private sealed record CompanyMissionDefinition(
        string Id,
        CompanyMissionKind Kind,
        string NameLoc,
        string DescriptionLoc,
        string ObjectiveLoc,
        int Reward,
        int Target,
        int TimeLimitMinutes,
        bool Repeatable = false);

    /// <summary>
    /// The company mission board. Values are intentionally in one place so balance passes do not have to hunt
    /// through UI code: targets are absolute balances, time limits run from acceptance, and every mission pays
    /// company value. The Yautja bracer task is repeatable so every recovered bracer can be rewarded.
    /// </summary>
    private static readonly CompanyMissionDefinition[] MissionCatalog =
    [
        new(
            "robotics-points",
            CompanyMissionKind.RoboticsPoints,
            "cmu-company-mission-robotics-name",
            "cmu-company-mission-robotics-desc",
            "cmu-company-mission-robotics-objective",
            Reward: 5,
            Target: 25,
            TimeLimitMinutes: 30,
            Repeatable: true),
        new(
            "chemistry-points",
            CompanyMissionKind.ChemistryPoints,
            "cmu-company-mission-chemistry-name",
            "cmu-company-mission-chemistry-desc",
            "cmu-company-mission-chemistry-objective",
            Reward: 5,
            Target: 20,
            TimeLimitMinutes: 30,
            Repeatable: true),
        new(
            "chemistry-scan-simple",
            CompanyMissionKind.ChemistryScanSimple,
            "cmu-company-mission-scan-simple-name",
            "cmu-company-mission-scan-simple-desc",
            "cmu-company-mission-scan-objective",
            Reward: 10,
            Target: 1,
            TimeLimitMinutes: 45,
            Repeatable: true),
        new(
            "chemistry-scan-complex",
            CompanyMissionKind.ChemistryScanComplex,
            "cmu-company-mission-scan-complex-name",
            "cmu-company-mission-scan-complex-desc",
            "cmu-company-mission-scan-objective",
            Reward: 10,
            Target: 1,
            TimeLimitMinutes: 75,
            Repeatable: true),
        new(
            "create-wendigo",
            CompanyMissionKind.CreateWendigo,
            "cmu-company-mission-wendigo-name",
            "cmu-company-mission-wendigo-desc",
            "cmu-company-mission-wendigo-objective",
            Reward: 50,
            Target: 1,
            TimeLimitMinutes: 90),
        new(
            "corrupted-hive-representative",
            CompanyMissionKind.CorruptedHiveRepresentative,
            "cmu-company-mission-hive-rep-name",
            "cmu-company-mission-hive-rep-desc",
            "cmu-company-mission-single-objective",
            Reward: 60,
            Target: 1,
            TimeLimitMinutes: 90),
        new(
            "corrupted-hive-alliance",
            CompanyMissionKind.CorruptedHiveAlliance,
            "cmu-company-mission-hive-alliance-name",
            "cmu-company-mission-hive-alliance-desc",
            "cmu-company-mission-single-objective",
            Reward: 20,
            Target: 1,
            TimeLimitMinutes: 90),
        new(
            "predator-bracer",
            CompanyMissionKind.PredatorBracer,
            "cmu-company-mission-bracer-name",
            "cmu-company-mission-bracer-desc",
            "cmu-company-mission-bracer-objective",
            Reward: 1,
            Target: 1,
            TimeLimitMinutes: 60,
            Repeatable: true),
    ];
}
