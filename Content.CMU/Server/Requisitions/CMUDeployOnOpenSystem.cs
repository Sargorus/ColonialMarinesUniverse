using Content.Shared.CMU14.Requisitions;
using Content.Shared.Interaction;
using Content.Shared.Storage.Components;
using Content.Shared.Tools.Systems;

namespace Content.Server.CMU14.Requisitions;

/// <summary>
/// Deploys the payload of a CMUDeployOnOpen crate when the crate is opened or
/// destroyed, so the product cannot be lost by prying the crate apart.
/// </summary>
public sealed partial class CMUDeployOnOpenSystem : EntitySystem
{
    private const string PryingQuality = "Prying";

    [Dependency] private SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMUDeployOnOpenComponent, StorageAfterOpenEvent>(OnStorageOpened);
        SubscribeLocalEvent<CMUDeployOnOpenComponent, InteractUsingEvent>(OnPried);
        SubscribeLocalEvent<CMUDeployOnOpenComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnStorageOpened(Entity<CMUDeployOnOpenComponent> ent, ref StorageAfterOpenEvent args)
    {
        // EntityStorageCloseOnMapInit opens every crate once while it is being
        // initialized. That is not a player unpacking the crate: deploying here
        // would spawn the payload in nullspace and delete the shipment before it
        // ever reaches the cargo elevator.
        if (args.User == null)
            return;

        Deploy(ent);
    }

    private void OnPried(Entity<CMUDeployOnOpenComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, PryingQuality))
            return;

        args.Handled = true;
        Deploy(ent);
    }

    private void OnTerminating(Entity<CMUDeployOnOpenComponent> ent, ref EntityTerminatingEvent args)
    {
        Deploy(ent);
    }

    private void Deploy(Entity<CMUDeployOnOpenComponent> ent)
    {
        if (ent.Comp.Deployed)
            return;

        // Do not spawn products on a map that is being cleaned up, and do not
        // materialize an off-map custom delivery crate that was cancelled.
        if (!TryComp(ent.Owner, out TransformComponent? xform)
            || xform.MapUid is not { } map
            || TerminatingOrDeleted(map)
            || EntityManager.IsQueuedForDeletion(map))
            return;

        ent.Comp.Deployed = true;
        var coordinates = xform.Coordinates;
        foreach (var prototype in ent.Comp.Spawn)
            Spawn(prototype, coordinates);

        if (!TerminatingOrDeleted(ent.Owner))
            QueueDel(ent.Owner);
    }
}
