using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Decals;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Decals;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Picks the nearest cleanable blood decal for a CMU cleanbot and stores its
/// grid coordinates under <see cref="TargetCoordinatesKey"/>.
/// </summary>
public sealed partial class CMUPickNearbyBloodDecalOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    private DecalSystem _decals = default!;
    private SharedTransformSystem _transform = default!;

    [DataField]
    public float Range = 10f;

    [DataField]
    public string TargetCoordinatesKey = "TargetCoordinates";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _decals = sysManager.GetEntitySystem<DecalSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<TransformComponent>(owner, out var xform))
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));

        var grid = _transform.GetGrid(xform.Coordinates);
        if (grid is not { } gridUid)
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));

        var local = _transform.WithEntityId(xform.Coordinates, gridUid).Position;
        var decals = _decals.GetDecalsInRange(gridUid, local, Range, IsBlood);
        if (decals.Count == 0)
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));

        var nearest = decals.MinBy(pair => (pair.Decal.Coordinates - local).LengthSquared());
        var coordinates = new EntityCoordinates(gridUid, nearest.Decal.Coordinates);

        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { TargetCoordinatesKey, coordinates },
        }));
    }

    public static bool IsBlood(Decal decal)
    {
        if (!decal.Cleanable
            || decal.Id.Contains("Xeno", StringComparison.OrdinalIgnoreCase)
            || decal.Id.Contains("Acid", StringComparison.OrdinalIgnoreCase))
            return false;

        // Bloody boot prints reuse the generic footprint decals, so clean all
        // footprint decals rather than only ids containing "Blood".
        return decal.Id.Contains("Blood", StringComparison.OrdinalIgnoreCase)
            || decal.Id.Contains("Footprint", StringComparison.OrdinalIgnoreCase);
    }
}
