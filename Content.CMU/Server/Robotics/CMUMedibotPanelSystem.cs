using Content.Shared.CMU14.Robotics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.EntitySystems;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Keeps the medical bot beaker slot locked unless its service panel is open.
/// </summary>
public sealed partial class CMUMedibotPanelSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMUMedibotComponent, MapInitEvent>(OnMapInit, after: [typeof(ItemSlotsSystem)]);
        SubscribeLocalEvent<CMUMedibotComponent, ToolOpenableDoAfterEventToggleOpen>(
            OnPanelToggled,
            after: [typeof(ToolOpenableSystem)]);
    }

    private void OnMapInit(Entity<CMUMedibotComponent> ent, ref MapInitEvent args)
    {
        SyncSlot(ent);
    }

    private void OnPanelToggled(Entity<CMUMedibotComponent> ent, ref ToolOpenableDoAfterEventToggleOpen args)
    {
        if (args.Cancelled)
            return;

        SyncSlot(ent);
    }

    private void SyncSlot(Entity<CMUMedibotComponent> ent)
    {
        if (!TryComp<ToolOpenableComponent>(ent, out var panel))
            return;

        _itemSlots.SetLock(ent.Owner, ent.Comp.BeakerSlotId, !panel.IsOpen);
    }
}
