using Content.Shared.CMU14.Squads;

namespace Content.Shared._RMC14.Marines.Squads;

public sealed partial class SquadSystem
{
    // CMU14: "Job (Squad)" for squads marked with CMUSquadTitleSuffixComponent.
    private string CMUFormatSquadTitle(Entity<SquadTeamComponent?> squad, string jobName)
    {
        var squadName = Name(squad);
        return HasComp<CMUSquadTitleSuffixComponent>(squad)
            ? $"{jobName} ({squadName})"
            : $"{squadName} {jobName}";
    }
}
