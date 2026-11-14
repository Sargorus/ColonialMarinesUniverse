using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Storage.Components;

namespace Content.Client.CMU14.ColonyEconomy;

/// <summary>
/// Stops client-side prediction from opening a PIN-protected safe for a frame
/// before the server can show the PIN dialog. The server still receives the
/// activation and opens the dialog.
/// </summary>
public sealed partial class CMUSafeClientSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMUSafeComponent, ActivateInWorldEvent>(
            OnActivate,
            before: [typeof(LockSystem)]);
    }

    private void OnActivate(Entity<CMUSafeComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!args.Complex || !ent.Comp.PinProtected)
            return;

        // An open safe should still close predictably; only suppress prediction
        // when the server would ask for a PIN instead of opening it.
        if (TryComp<EntityStorageComponent>(ent.Owner, out var storage) && storage.Open)
            return;

        args.Handled = true;
    }
}
