using Robust.Shared.GameStates;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Makes a bot periodically say a Weyland-Yutani slogan.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CMUBotSloganComponent : Component
{
    [DataField]
    public string Phrase = "cmu-bot-slogan-weyu";

    [DataField]
    public TimeSpan MinInterval = TimeSpan.FromMinutes(4);

    [DataField]
    public TimeSpan MaxInterval = TimeSpan.FromMinutes(8);

    public TimeSpan NextSpeak;
}
