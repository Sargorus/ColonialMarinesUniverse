using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.DroneOperator;

/// <summary>Appearance keys used by the drone android frame to show installed limbs.</summary>
[Serializable, NetSerializable]
public enum CMUDroneFrameVisuals : byte
{
    Head,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}
