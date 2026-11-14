using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Robust.Shared.Containers;

namespace Content.Shared.CMU14.Robotics;

/// <summary>
/// Prevents an entity marked with <see cref="CMUHandsItemBlockComponent"/> from picking up
/// items and prevents anything from being inserted into its hand containers.
/// </summary>
public sealed partial class CMUHandsItemBlockSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMUHandsItemBlockComponent, PickupAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<CMUHandsItemBlockComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnPickupAttempt(Entity<CMUHandsItemBlockComponent> ent, ref PickupAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnInsertAttempt(Entity<CMUHandsItemBlockComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (!_hands.TryGetHand(ent.Owner, args.Container.ID, out _))
            return;

        args.Cancel();
    }
}
