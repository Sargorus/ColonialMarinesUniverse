using Content.Server.Decals;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Decals;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Removes cleanable blood decals at the coordinates selected by
/// <see cref="CMUPickNearbyBloodDecalOperator"/>.
/// </summary>
public sealed partial class CMUCleanBloodDecalOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    private DecalSystem _decals = default!;
    private SharedAudioSystem _audio = default!;
    private SharedTransformSystem _transform = default!;

    [DataField]
    public string TargetCoordinatesKey = "TargetCoordinates";

    [DataField]
    public float Range = 0.75f;

    [DataField]
    public EntProtoId MoppedEffect = "PuddleSparkle";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _audio = sysManager.GetEntitySystem<SharedAudioSystem>();
        _decals = sysManager.GetEntitySystem<DecalSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out var coordinates, _entities)
            || !coordinates.IsValid(_entities))
            return HTNOperatorStatus.Failed;

        var grid = _transform.GetGrid(coordinates);
        if (grid is not { } gridUid)
            return HTNOperatorStatus.Failed;

        var local = _transform.WithEntityId(coordinates, gridUid).Position;
        var decals = _decals.GetDecalsInRange(gridUid, local, Range, CMUPickNearbyBloodDecalOperator.IsBlood);
        if (decals.Count == 0)
            return HTNOperatorStatus.Finished;

        foreach (var (index, _) in decals)
            _decals.RemoveDecal(gridUid, index);

        _audio.PlayPvs(
            new SoundPathSpecifier("/Audio/Effects/Fluids/watersplash.ogg"),
            owner);
        _entities.SpawnEntity(MoppedEffect, coordinates);

        return HTNOperatorStatus.Finished;
    }
}
