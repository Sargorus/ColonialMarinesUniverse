namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// A printed robotics work-order slip. It must be placed on the corporate cargo
/// platform so the order console can match the delivery to the accepted order.
/// </summary>
[RegisterComponent]
public sealed partial class CMURoboticsOrderSlipComponent : Component
{
    [DataField]
    public int OrderId;

    [DataField]
    public string TargetName = string.Empty;

    [DataField]
    public int Amount = 1;

    [DataField]
    public int Reward;

    /// <summary>
    /// The rendered order text. Kept so the paper can re-apply it after
    /// PaperComponent's own map initialization.
    /// </summary>
    [DataField]
    public string Content = string.Empty;
}
