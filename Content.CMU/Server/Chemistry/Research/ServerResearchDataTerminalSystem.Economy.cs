using Content.Server.Administration.Logs;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

// All research cash is company income, regardless of which faction made the discovery.
public sealed partial class ServerResearchDataTerminalSystem
{
    private void DepositResearchCash(ReagentPrototype proto, string faction, EntityUid? scanner)
    {
        var amount = ResearchCashRewardMult * proto.Reward;
        if (amount <= 0f)
            return;

        _corporateConsole.AddToCorporateBudget(amount);

        var source = scanner is { } scannerUid && scannerUid.IsValid()
            ? ToPrettyString(scannerUid)
            : "the research archive";
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{source} completed research chemical {proto.ID} for {faction}; " +
            $"deposited {amount:0} dollars into the company account");
    }
}
