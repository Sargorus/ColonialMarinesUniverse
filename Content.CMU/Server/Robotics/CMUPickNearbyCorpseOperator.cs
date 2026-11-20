using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Atmos.Rotting;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Traits.Assorted;
using Content.Shared._RMC14.Medical.CPR;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Picks a dead, intact member of a faction the medical bot serves and sets
/// the movement target for a CPR attempt.
/// </summary>
public sealed partial class CMUPickNearbyCorpseOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private CPRSystem _cpr = default!;
    private PathfindingSystem _pathfinding = default!;

    [DataField]
    public string TargetKey = "Target";

    [DataField]
    public string TargetMoveKey = "TargetCoordinates";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cpr = sysManager.GetEntitySystem<CPRSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<CMUMedibotComponent>(owner, out var medibot))
            return (false, null);

        if (!blackboard.TryGetValue<IEnumerable<KeyValuePair<EntityUid, float>>>("TargetList", out var patients, _entities))
            return (false, null);

        foreach (var (entity, _) in patients)
        {
            if (!_entities.TryGetComponent<MobStateComponent>(entity, out var mobState)
                || mobState.CurrentState != MobState.Dead
                || _entities.HasComponent<RottingComponent>(entity)
                || _entities.HasComponent<UnrevivableComponent>(entity)
                || _cpr.IsRecentlyCPRed(entity)
                || !CMUMedibotTargeting.IsValidPatient(_entities, entity, medibot))
                continue;

            if (_entities.TryGetComponent<CMUMedibotReservationComponent>(entity, out var reservation)
                && reservation.Bot != owner
                && _timing.CurTime < reservation.Expires)
                continue;

            var ownerXform = _entities.GetComponent<TransformComponent>(owner);
            var targetXform = _entities.GetComponent<TransformComponent>(entity);
            var path = await _pathfinding.GetPath(
                owner,
                ownerXform.Coordinates,
                targetXform.Coordinates,
                SharedInteractionSystem.InteractionRange - 1f,
                cancelToken);

            if (path.Result != PathResult.Path)
                continue;

            var reserved = _entities.EnsureComponent<CMUMedibotReservationComponent>(entity);
            reserved.Bot = owner;
            reserved.Expires = _timing.CurTime + TimeSpan.FromSeconds(15);

            return (true, new Dictionary<string, object>
            {
                { TargetKey, entity },
                { TargetMoveKey, targetXform.Coordinates },
                { NPCBlackboard.PathfindKey, path },
            });
        }

        return (false, null);
    }
}
