using Content.Shared.Atmos;
using Content.Shared.DeviceLinking;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Atmos;

/// <summary>
/// A vent that holds a gas tank or canister and releases its contents into the
/// surrounding atmosphere when opened by a signal.
/// </summary>
[RegisterComponent]
[Access(typeof(SignalGasVentSystem))]
public sealed partial class SignalGasVentComponent : Component
{
    /// <summary>
    /// Item slot that holds the gas tank or canister.
    /// </summary>
    [DataField]
    public string SlotId = "tank";

    /// <summary>
    /// Signal port that opens the vent.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> OpenPort = "Open";

    /// <summary>
    /// Signal port that closes the vent.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> ClosePort = "Close";

    /// <summary>
    /// Signal port that toggles the vent.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> TogglePort = "Toggle";

    /// <summary>
    /// Maximum pressure the vent can push gas out at.
    /// </summary>
    [DataField]
    public float ReleasePressure = Atmospherics.OneAtmosphere;

    /// <summary>
    /// Effective opening area of the vent in square meters.
    /// </summary>
    [DataField]
    public float ReleaseArea = 0.05f;

    /// <summary>
    /// How often the release sound is played while gas is flowing.
    /// </summary>
    [DataField]
    public TimeSpan ReleaseSoundInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Sound played while gas is flowing.
    /// </summary>
    [DataField]
    public SoundSpecifier? ReleaseSound = new SoundPathSpecifier("/Audio/Effects/spray.ogg")
    {
        Params = AudioParams.Default.WithVolume(-8f),
    };

    /// <summary>
    /// Whether the vent is currently open.
    /// </summary>
    [ViewVariables]
    public bool Enabled;

    /// <summary>
    /// The next time the release sound may be played.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextReleaseSound;

    /// <summary>
    /// The next time the held tank's state should be dirtied for the client.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextTankDirty;
}
