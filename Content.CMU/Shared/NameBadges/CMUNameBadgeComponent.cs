using Robust.Shared.GameStates;

namespace Content.Shared.CMU14.NameBadges;

/// <summary>
/// CMU14: a name badge that shows the name of the character it belongs to.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CMUNameBadgeComponent : Component
{
    /// <summary>
    /// The wearer's name, set when the badge is awarded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? Name;
}
