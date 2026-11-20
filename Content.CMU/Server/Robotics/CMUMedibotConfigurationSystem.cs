using Content.Shared.CMU14.Configuration;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Server side of the medibot configuration key UI. Kept separate from the
/// drone configuration system so medibot and drone behaviour can evolve
/// independently.
/// </summary>
public sealed partial class CMUMedibotConfigurationSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<CMUMedibotComponent>(CMUMedibotConfigurationUi.Key, subs =>
        {
            subs.Event<CMUMedibotConfigurationApplyMsg>(OnApply);
        });
    }

    public void Open(Entity<CMUMedibotComponent> ent, EntityUid user, EntityUid key)
    {
        ent.Comp.ConfigUser = user;
        ent.Comp.ConfigKey = key;
        UpdateUi(ent.Owner, ent.Comp);
        _ui.TryOpenUi(ent.Owner, CMUMedibotConfigurationUi.Key, user);
    }

    private void OnApply(Entity<CMUMedibotComponent> ent, ref CMUMedibotConfigurationApplyMsg msg)
    {
        if (ent.Comp.ConfigUser != msg.Actor
            || ent.Comp.ConfigKey is not { } key
            || !_hands.IsHolding(msg.Actor, key))
            return;

        ent.Comp.InjectAmount = FixedPoint2.New(Math.Clamp(msg.InjectAmount, 1, 50));
        ent.Comp.InjectOnBrute = msg.InjectOnBrute;
        ent.Comp.InjectOnBurn = msg.InjectOnBurn;
        ent.Comp.InjectOnCritical = msg.InjectOnCritical;

        var factions = new HashSet<string>();
        foreach (var id in msg.Factions)
        {
            if (_proto.TryIndex<NpcFactionPrototype>(id, out var faction) && !faction.Abstract)
                factions.Add(faction.ID);
        }
        ent.Comp.TreatFactions = factions;

        UpdateUi(ent.Owner, ent.Comp);
        _popup.PopupEntity(Loc.GetString("cmu-medibot-config-applied"), ent, msg.Actor);
    }

    private void UpdateUi(EntityUid medibot, CMUMedibotComponent component)
    {
        var beaker = _itemSlots.GetItemOrNull(medibot, component.BeakerSlotId);
        var hasBeaker = false;
        var beakerVolume = FixedPoint2.Zero;

        if (beaker is { } beakerUid
            && _solutions.TryGetSolution(beakerUid, component.BeakerSolution, out _, out var solution))
        {
            hasBeaker = true;
            beakerVolume = solution.Volume;
        }

        var factions = new List<CMUConfigurationOption>();
        foreach (var faction in _proto.EnumeratePrototypes<NpcFactionPrototype>())
        {
            if (faction.Abstract)
                continue;

            var name = string.IsNullOrEmpty(faction.Name)
                ? faction.ID
                : faction.Name;
            factions.Add(new CMUConfigurationOption(faction.ID, name));
        }
        factions.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        var state = new CMUMedibotConfigurationBuiState(
            component.InjectAmount.Int(),
            component.InjectOnBrute,
            component.InjectOnBurn,
            component.InjectOnCritical,
            hasBeaker,
            beakerVolume,
            factions,
            component.TreatFactions);

        _ui.SetUiState(medibot, CMUMedibotConfigurationUi.Key, state);
    }
}
