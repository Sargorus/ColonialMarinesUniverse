using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Atmos.Rotting;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Traits.Assorted;
using Content.Shared._RMC14.Medical.CPR;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Starts a CPR do-after for a medical bot on the corpse selected by
/// <see cref="CMUPickNearbyCorpseOperator"/>.
/// </summary>
public sealed partial class CMUCPROperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    private CPRSystem _cpr = default!;

    [DataField]
    public string TargetKey = "Target";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cpr = sysManager.GetEntitySystem<CPRSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entities)
            || target == EntityUid.Invalid
            || _entities.Deleted(target)
            || _entities.HasComponent<RottingComponent>(target)
            || _entities.HasComponent<UnrevivableComponent>(target))
            return HTNOperatorStatus.Failed;

        if (!_cpr.TryStartCPR(owner, target))
            return HTNOperatorStatus.Failed;

        _entities.RemoveComponent<CMUMedibotReservationComponent>(target);
        return HTNOperatorStatus.Finished;
    }
}
