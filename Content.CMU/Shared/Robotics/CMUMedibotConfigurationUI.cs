using Content.Shared.CMU14.Configuration;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Robotics;

[Serializable, NetSerializable]
public enum CMUMedibotConfigurationUi : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class CMUMedibotConfigurationBuiState : BoundUserInterfaceState
{
    public int InjectAmount { get; }
    public bool InjectOnBrute { get; }
    public bool InjectOnBurn { get; }
    public bool InjectOnCritical { get; }
    public bool HasBeaker { get; }
    public FixedPoint2 BeakerVolume { get; }
    public List<CMUConfigurationOption> Factions { get; }
    public HashSet<string> SelectedFactions { get; }

    public CMUMedibotConfigurationBuiState(
        int injectAmount,
        bool injectOnBrute,
        bool injectOnBurn,
        bool injectOnCritical,
        bool hasBeaker,
        FixedPoint2 beakerVolume,
        List<CMUConfigurationOption> factions,
        HashSet<string> selectedFactions)
    {
        InjectAmount = injectAmount;
        InjectOnBrute = injectOnBrute;
        InjectOnBurn = injectOnBurn;
        InjectOnCritical = injectOnCritical;
        HasBeaker = hasBeaker;
        BeakerVolume = beakerVolume;
        Factions = factions;
        SelectedFactions = selectedFactions;
    }
}

[Serializable, NetSerializable]
public sealed class CMUMedibotConfigurationApplyMsg : BoundUserInterfaceMessage
{
    public int InjectAmount { get; }
    public bool InjectOnBrute { get; }
    public bool InjectOnBurn { get; }
    public bool InjectOnCritical { get; }
    public HashSet<string> Factions { get; }

    public CMUMedibotConfigurationApplyMsg(
        int injectAmount,
        bool injectOnBrute,
        bool injectOnBurn,
        bool injectOnCritical,
        HashSet<string> factions)
    {
        InjectAmount = injectAmount;
        InjectOnBrute = injectOnBrute;
        InjectOnBurn = injectOnBurn;
        InjectOnCritical = injectOnCritical;
        Factions = factions;
    }
}
