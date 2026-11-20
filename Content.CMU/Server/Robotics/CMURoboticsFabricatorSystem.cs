using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Lathe;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Power;
using Content.Shared.CMU14.Robotics;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Powers the robotics fabricator from an inserted WL-6 fuel cell and gates its
/// interface behind the research skill.
/// </summary>
public sealed partial class CMURoboticsFabricatorSystem : EntitySystem
{
    private const string PryingQuality = "Prying";

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SkillsSystem _skills = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMURoboticsFabricatorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, EntInsertedIntoContainerMessage>(OnCellInserted);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, EntRemovedFromContainerMessage>(OnCellRemoved);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, CMURoboticsFabricatorInsertCellDoAfterEvent>(OnInsertCell);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, CMURoboticsFabricatorRemoveCellDoAfterEvent>(OnRemoveCell);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, ActivatableUIOpenAttemptEvent>(OnUiOpenAttempt);
        SubscribeLocalEvent<CMURoboticsFabricatorComponent, LatheGetRecipesEvent>(OnGetRecipes);
    }

    private void OnInit(Entity<CMURoboticsFabricatorComponent> ent, ref ComponentInit args)
    {
        EnsureCellContainer(ent);
    }

    private void OnMapInit(Entity<CMURoboticsFabricatorComponent> ent, ref MapInitEvent args)
    {
        // List-valued prototype inheritance appends parent entries. Drop the
        // autolathe recipe packs that come from CMAutolathe and keep only the
        // robotics pack.
        if (TryComp<LatheComponent>(ent.Owner, out var lathe))
        {
            lathe.StaticPacks.Clear();
            lathe.StaticPacks.Add(ent.Comp.ProductionRecipePack);
            lathe.DynamicPacks.Clear();
        }

        UpdatePower(ent);
    }

    private void OnInteractUsing(Entity<CMURoboticsFabricatorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<CMURoboticsRecipeCartridgeComponent>(args.Used))
        {
            args.Handled = true;
            TryUnlockCartridge(ent, args.Used, args.User);
            return;
        }

        var container = EnsureCellContainer(ent);

        if (HasComp<RMCFusionCellComponent>(args.Used))
        {
            args.Handled = true;

            if (container.ContainedEntity != null)
            {
                _popup.PopupEntity(
                    Loc.GetString("cmu-robotics-fabricator-cell-already", ("fabricator", ent.Owner)),
                    ent.Owner,
                    args.User,
                    PopupType.SmallCaution);
                return;
            }

            var doAfter = new DoAfterArgs(
                EntityManager,
                args.User,
                ent.Comp.InsertDelay,
                new CMURoboticsFabricatorInsertCellDoAfterEvent(),
                ent.Owner,
                target: ent.Owner,
                used: args.Used)
            {
                BreakOnMove = true,
                BreakOnDropItem = true,
                NeedHand = true,
                DuplicateCondition = DuplicateConditions.SameEvent,
            };

            if (_doAfter.TryStartDoAfter(doAfter))
            {
                _popup.PopupEntity(
                    Loc.GetString("cmu-robotics-fabricator-cell-insert-start", ("fabricator", ent.Owner)),
                    ent.Owner,
                    args.User);
            }

            return;
        }

        if (_tool.HasQuality(args.Used, PryingQuality) && container.ContainedEntity != null)
        {
            args.Handled = true;
            StartRemoveCell(ent, args.User, args.Used);
        }
    }

    private void OnGetVerbs(Entity<CMURoboticsFabricatorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;

        if (HasCell(ent))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cmu-robotics-fabricator-cell-remove-verb"),
                Priority = 2,
                Act = () => StartRemoveCell(ent, user),
            });
        }
    }

    private void StartRemoveCell(
        Entity<CMURoboticsFabricatorComponent> ent,
        EntityUid user,
        EntityUid? used = null)
    {
        var container = EnsureCellContainer(ent);
        if (container.ContainedEntity == null)
            return;

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            ent.Comp.RemoveDelay,
            new CMURoboticsFabricatorRemoveCellDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: used)
        {
            BreakOnMove = true,
            BreakOnDropItem = true,
            NeedHand = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-robotics-fabricator-cell-remove-start", ("fabricator", ent.Owner)),
                ent.Owner,
                user);
        }
    }

    private void OnCellInserted(Entity<CMURoboticsFabricatorComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.CellContainerSlot)
            UpdatePower(ent);
    }

    private void OnCellRemoved(Entity<CMURoboticsFabricatorComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.CellContainerSlot)
            UpdatePower(ent);
    }

    private void OnInsertCell(Entity<CMURoboticsFabricatorComponent> ent, ref CMURoboticsFabricatorInsertCellDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } cell)
            return;

        args.Handled = true;

        if (!HasComp<RMCFusionCellComponent>(cell))
            return;

        var container = EnsureCellContainer(ent);
        if (!_container.Insert(cell, container))
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-robotics-fabricator-cell-insert-fail", ("fabricator", ent.Owner)),
                ent.Owner,
                args.User,
                PopupType.SmallCaution);
            return;
        }

        UpdatePower(ent);
        _popup.PopupEntity(
            Loc.GetString("cmu-robotics-fabricator-cell-insert-finish", ("fabricator", ent.Owner)),
            ent.Owner,
            args.User);
    }

    private void OnRemoveCell(Entity<CMURoboticsFabricatorComponent> ent, ref CMURoboticsFabricatorRemoveCellDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var container = EnsureCellContainer(ent);
        if (container.ContainedEntity is not { } cell)
            return;

        if (!_container.Remove(cell, container))
            return;

        _hands.TryPickupAnyHand(args.User, cell);
        UpdatePower(ent);
        _popup.PopupEntity(
            Loc.GetString("cmu-robotics-fabricator-cell-remove-finish", ("fabricator", ent.Owner)),
            ent.Owner,
            args.User);
    }

    private void OnExamined(Entity<CMURoboticsFabricatorComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(CMURoboticsFabricatorComponent)))
        {
            var inserted = HasCell(ent);
            args.PushMarkup(Loc.GetString(
                inserted
                    ? "cmu-robotics-fabricator-examine-cell-inserted"
                    : "cmu-robotics-fabricator-examine-cell-empty"));

            args.PushMarkup(Loc.GetString(
                "cmu-robotics-fabricator-examine-research",
                ("level", ent.Comp.ResearchLevel)));

            args.PushMarkup(Loc.GetString(
                "cmu-robotics-fabricator-examine-unlocked",
                ("count", ent.Comp.UnlockedPacks.Count)));
        }
    }

    private void OnUiOpenAttempt(Entity<CMURoboticsFabricatorComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (_skills.HasSkill(args.User, ent.Comp.ResearchSkill, ent.Comp.ResearchLevel))
            return;

        args.Cancel();

        if (!args.Silent)
        {
            _popup.PopupEntity(
                Loc.GetString(
                    "cmu-robotics-fabricator-skill-required",
                    ("level", ent.Comp.ResearchLevel)),
                ent.Owner,
                args.User,
                PopupType.SmallCaution);
        }
    }

    /// <summary>
    /// Consumes a recipe cartridge and permanently unlocks its pack on this
    /// fabricator. The cartridge item is deleted so the unlock is one-way.
    /// </summary>
    public bool TryUnlockCartridge(
        Entity<CMURoboticsFabricatorComponent> ent,
        EntityUid cartridge,
        EntityUid user)
    {
        if (!TryComp<CMURoboticsRecipeCartridgeComponent>(cartridge, out var cartridgeComp))
            return false;

        var alreadyKnown = ent.Comp.UnlockedPacks.Contains(cartridgeComp.Pack);
        if (!alreadyKnown)
            ent.Comp.UnlockedPacks.Add(cartridgeComp.Pack);

        _lathe.UpdateUserInterfaceState(ent.Owner);
        _popup.PopupEntity(
            Loc.GetString(
                alreadyKnown
                    ? "cmu-robotics-fabricator-cartridge-known"
                    : "cmu-robotics-fabricator-cartridge-unlocked",
                ("cartridge", Name(cartridge))),
            ent.Owner,
            user);
        QueueDel(cartridge);
        return true;
    }

    private void OnGetRecipes(Entity<CMURoboticsFabricatorComponent> ent, ref LatheGetRecipesEvent args)
    {
        if (args.Lathe != ent.Owner)
            return;

        foreach (var packId in ent.Comp.UnlockedPacks)
        {
            if (!_prototypes.TryIndex(packId, out var pack))
                continue;

            foreach (var recipe in pack.Recipes)
                args.Recipes.Add(recipe);
        }
    }

    private ContainerSlot EnsureCellContainer(Entity<CMURoboticsFabricatorComponent> ent)
    {
        return _container.EnsureContainer<ContainerSlot>(ent.Owner, ent.Comp.CellContainerSlot);
    }

    private bool HasCell(Entity<CMURoboticsFabricatorComponent> ent)
    {
        var container = EnsureCellContainer(ent);
        return container.ContainedEntity != null;
    }

    private void UpdatePower(Entity<CMURoboticsFabricatorComponent> ent)
    {
        if (!TryComp<ApcPowerReceiverComponent>(ent.Owner, out var receiver))
            return;

        _power.SetPowerDisabled(ent.Owner, !HasCell(ent), receiver);
    }
}
