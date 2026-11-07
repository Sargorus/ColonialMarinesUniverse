using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CMU14.Requisitions;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.FixedPoint;
using Content.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Requisitions;

/// <summary>
/// Rewrites the corporate barrier's inherited access, fixture layers and health
/// after prototype composition. List-valued YAML inheritance appends parent
/// entries, so doing this in YAML alone would leave the marine access tag, the
/// WallLayer and the old 100-damage threshold in place.
/// </summary>
public sealed partial class CMUWYSecurityBarrierSystem : EntitySystem
{
    private const int BarrierLayer =
        (int) (CollisionGroup.HighImpassable |
               CollisionGroup.MidImpassable |
               CollisionGroup.LowImpassable |
               CollisionGroup.BulletImpassable);

    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMUWYSecurityBarrierComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<CMUWYSecurityBarrierComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<AccessReaderComponent>(ent.Owner, out var access))
        {
            _access.TrySetAccesses(
                (ent.Owner, access),
                new List<HashSet<ProtoId<AccessLevelPrototype>>>
                {
                    new() { ent.Comp.Access },
                },
                updateOriginal: true);
        }

        if (_fixtures.GetFixtureOrNull(ent.Owner, "barrier") is { } barrier)
            _physics.SetCollisionLayer(ent.Owner, "barrier", barrier, BarrierLayer);

        // BaseStructure adds a generic fix1 fixture; the deployable barrier uses
        // its own base/barrier pair, so drop the inherited one.
        _fixtures.DestroyFixture(ent.Owner, "fix1");

        // Replace the inherited CMDeployableBarrier 100-damage threshold.
        var destructible = EnsureComp<DestructibleComponent>(ent.Owner);
        _destructible.ReplaceThresholds(
            (ent.Owner, destructible),
            new List<DamageThreshold>
            {
                new()
                {
                    Trigger = new DamageTrigger { Damage = FixedPoint2.New(ent.Comp.HitPoints) },
                    Behaviors = new List<IThresholdBehavior>
                    {
                        new DoActsBehavior { Acts = ThresholdActs.Destruction },
                    },
                },
            });
    }
}
