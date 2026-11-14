using Content.Shared.CMU14.DroneOperator;
using Content.Shared.Humanoid;
using Content.Shared.NPC.Components;
using Content.Shared._RMC14.Synth;
using Robust.Shared.GameObjects;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Shared patient eligibility rules for CMU medibots. Keeping the faction,
/// humanoid and synthetic checks in one place prevents the injectable and CPR
/// operators from drifting apart.
/// </summary>
public static class CMUMedibotTargeting
{
    public static bool IsValidPatient(
        IEntityManager entities,
        EntityUid target,
        CMUMedibotComponent component)
    {
        if (!entities.TryGetComponent<NpcFactionMemberComponent>(target, out var factions)
            || !entities.HasComponent<HumanoidProfileComponent>(target)
            || entities.HasComponent<CMUDroneAndroidComponent>(target)
            || entities.HasComponent<SynthComponent>(target))
            return false;

        foreach (var faction in factions.Factions)
        {
            if (component.TreatFactions.Contains(faction.Id))
                return true;
        }

        return false;
    }
}
