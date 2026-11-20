using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Makes a medibot draw its injection from a beaker item slot and lets a drone
/// configuration key tune the dose and the damage types that trigger it.
/// </summary>
[RegisterComponent]
public sealed partial class CMUMedibotComponent : Component
{
    [DataField]
    public string BeakerSlotId = "cmu-medibot-beaker";

    [DataField]
    public string BeakerSolution = "beaker";

    [DataField]
    public FixedPoint2 InjectAmount = 15;

    [DataField]
    public bool InjectOnBrute = true;

    [DataField]
    public bool InjectOnBurn = true;

    [DataField]
    public bool InjectOnCritical = true;

    /// <summary>
    /// Faction ids whose humanoid members this bot is allowed to serve.
    /// Normal humanoids inherit <c>AUColonist</c>, so that faction is served
    /// out of the box alongside the Weyland-Yutani crew that builds the bot.
    /// </summary>
    [DataField]
    public HashSet<string> TreatFactions = new() { "AUWeYu", "AUColonist" };

    public EntityUid? ConfigUser;

    public EntityUid? ConfigKey;
}
