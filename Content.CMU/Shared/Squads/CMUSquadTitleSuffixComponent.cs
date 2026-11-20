using Robust.Shared.GameStates;

namespace Content.Shared.CMU14.Squads;

/// <summary>
/// CMU14: marks a squad whose member titles use "Job (Squad)" instead of the default
/// "Squad Job". Applied only to the WY complex squads so other squads keep their titles.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CMUSquadTitleSuffixComponent : Component;
