using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Decals;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Silicons.Bots;
using Content.Shared.Tools.Components;
using Content.Shared.UserInterface;
using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Medical.CPR;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMURoboticsBotsTest : GameTest
{
    [Test]
    public async Task FabricatorBuildsBotsWithoutStartingBeaker()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Assert.That(prototypes.HasIndex<EntityPrototype>("CMUCleanBot"), Is.True);
            Assert.That(prototypes.HasIndex<EntityPrototype>("CMUHugBot"), Is.True);

            var pack = prototypes.Index<LatheRecipePackPrototype>("CMURoboticsFabricatorRecipes");
            Assert.That(pack.Recipes.Select(recipe => recipe.Id), Does.Contain("CMURoboticsCleanBot"));
            Assert.That(pack.Recipes.Select(recipe => recipe.Id), Does.Contain("CMURoboticsHugBot"));
            Assert.That(pack.Recipes.Select(recipe => recipe.Id), Does.Contain("CMURoboticsMedibot"));

            var bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords);
            Assert.That(SEntMan.HasComponent<CMUMedibotComponent>(bot), Is.True);
            Assert.That(SEntMan.HasComponent<MedibotComponent>(bot), Is.True);
            Assert.That(SEntMan.HasComponent<MarineComponent>(bot), Is.True);
            Assert.That(SEntMan.HasComponent<HandsComponent>(bot), Is.True);
            Assert.That(SEntMan.HasComponent<CMUHandsItemBlockComponent>(bot), Is.True);
            Assert.That(SEntMan.System<ItemSlotsSystem>().GetItemOrNull(bot, "cmu-medibot-beaker"), Is.Null,
                "The fabricator must produce the medical bot without a beaker.");

            var panel = SComp<ToolOpenableComponent>(bot);
            Assert.That(panel.IsOpen, Is.False, "The service panel must start closed.");
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryGetSlot(bot, "cmu-medibot-beaker", out var slot), Is.True);
            Assert.That(slot!.Locked, Is.True, "The beaker slot must start locked behind the closed panel.");
        });
    }

    [Test]
    public async Task MedibotInjectsAnyBeakerAndKeyChangesSettings()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            var bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords.Offset(new(1, 0)));
            var patient = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(2, 0)));
            SEntMan.EnsureComponent<NpcFactionMemberComponent>(patient).Factions.Add("AUWeYu");

            OpenMedibotPanel(bot);
            InsertTricordBeaker(bot, user, map.GridCoords);

            var damage = new DamageSpecifier
            {
                DamageDict =
                {
                    ["Blunt"] = 20,
                },
            };
            SEntMan.System<DamageableSystem>().TryChangeDamage(patient, damage, ignoreResistances: true);

            var medibot = SComp<MedibotComponent>(bot);
            Assert.That(SEntMan.System<MedibotSystem>().TryInject((bot, medibot), patient), Is.True,
                "A medibot with an inserted beaker must be able to inject a brute-damaged patient.");

            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetInjectableSolution(patient, out var injectable, out _), Is.True);
            Assert.That(injectable!.Value.Comp.Solution.GetTotalPrototypeQuantity("CMTricordrazine"),
                Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(SEntMan.System<MedibotSystem>().CheckInjectable((bot, medibot), patient, manual: true), Is.False,
                "A patient must not be serviced by another medibot immediately after an injection.");

            var key = SEntMan.SpawnEntity("CMUDroneConfigurationKey", map.GridCoords.Offset(new(-1, 0)));
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, key), Is.True);

            var afterInteract = new AfterInteractEvent(
                user,
                key,
                bot,
                SEntMan.GetComponent<TransformComponent>(bot).Coordinates,
                true);
            SEntMan.EventBus.RaiseLocalEvent(key, afterInteract);
            Assert.That(afterInteract.Handled, Is.True);

            var medibotComponent = SComp<CMUMedibotComponent>(bot);
            Assert.That(medibotComponent.ConfigUser, Is.EqualTo(user));

            SEntMan.System<SharedUserInterfaceSystem>().RaiseUiMessage(
                bot,
                CMUMedibotConfigurationUi.Key,
                new CMUMedibotConfigurationApplyMsg(25, false, true, false, new HashSet<string> { "GOVFOR" })
                {
                    Actor = user,
                });

            Assert.That(medibotComponent.InjectAmount.Int(), Is.EqualTo(25));
            Assert.That(medibotComponent.InjectOnBrute, Is.False);
            Assert.That(medibotComponent.InjectOnBurn, Is.True);
            Assert.That(medibotComponent.InjectOnCritical, Is.False);
            Assert.That(medibotComponent.TreatFactions, Is.EquivalentTo(new[] { "GOVFOR" }));
        });
    }

    [TestCase("CMBeaker")]
    [TestCase("CMBeakerLarge")]
    [TestCase("RMCBeakerHighCapacity")]
    public async Task MedibotInjectsRmcBeaker(string beakerPrototype)
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            var bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords.Offset(new(1, 0)));
            var patient = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(2, 0)));
            SEntMan.EnsureComponent<NpcFactionMemberComponent>(patient).Factions.Add("AUWeYu");

            OpenMedibotPanel(bot);
            InsertTricordBeaker(bot, user, map.GridCoords, beakerPrototype);

            var damage = new DamageSpecifier
            {
                DamageDict =
                {
                    ["Blunt"] = 20,
                },
            };
            SEntMan.System<DamageableSystem>().TryChangeDamage(patient, damage, ignoreResistances: true);

            var medibot = SComp<MedibotComponent>(bot);
            Assert.That(SEntMan.System<MedibotSystem>().TryInject((bot, medibot), patient), Is.True,
                "An RMC beaker must also work in the CMU medibot.");
        });
    }

    [Test]
    public async Task MedibotTreatsDefaultColonistPatient()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            var bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords.Offset(new(1, 0)));

            var factions = SComp<NpcFactionMemberComponent>(patient);
            Assert.That(factions.Factions.Select(faction => faction.Id), Does.Contain("AUColonist"),
                "A normal human must carry the default colonist faction.");

            var component = SComp<CMUMedibotComponent>(bot);
            Assert.That(CMUMedibotTargeting.IsValidPatient(SEntMan, patient, component), Is.True,
                "The default medical bot must be able to treat a normal colonist.");
        });
    }

    [TestCase("Beaker", "Blunt")]
    [TestCase("Beaker", "Heat")]
    [TestCase("CMBeaker", "Blunt")]
    [TestCase("CMBeaker", "Heat")]
    public async Task MedibotAutomaticInjectionUsesBeaker(string beakerPrototype, string damageType)
    {
        var map = await Pair.CreateTestMap();
        EntityUid bot = default;
        EntityUid patient = default;

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords.Offset(new(1, 0)));
            patient = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(2, 0)));

            OpenMedibotPanel(bot);
            InsertTricordBeaker(bot, user, map.GridCoords, beakerPrototype);

            var damage = new DamageSpecifier
            {
                DamageDict =
                {
                    [damageType] = 20,
                },
            };
            SEntMan.System<DamageableSystem>().TryChangeDamage(patient, damage, ignoreResistances: true);

            var medibot = SComp<MedibotComponent>(bot);
            Assert.That(SEntMan.System<MedibotSystem>().CheckInjectable((bot, medibot), patient, manual: true), Is.True,
                "The automatic interaction path must accept an alive damaged patient.");

            // This is the same empty-hand interaction the HTN InteractWithOperator
            // uses. The patient is alive, so its interaction popup is also active.
            SEntMan.System<SharedInteractionSystem>().InteractHand(bot, patient);
        });

        await Pair.RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetInjectableSolution(patient, out var injectable, out _), Is.True);
            Assert.That(injectable!.Value.Comp.Solution.GetTotalPrototypeQuantity("CMTricordrazine"),
                Is.GreaterThan(FixedPoint2.Zero),
                "The medibot's automatic interaction must inject the beaker's contents.");
        });
    }

    [TestCase("Beaker")]
    [TestCase("CMBeaker")]
    public async Task MedibotInjectsCriticalPatientFromBeaker(string beakerPrototype)
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            var bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords.Offset(new(1, 0)));
            var patient = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(2, 0)));

            OpenMedibotPanel(bot);
            InsertTricordBeaker(bot, user, map.GridCoords, beakerPrototype);

            var cmu = SComp<CMUMedibotComponent>(bot);
            cmu.InjectOnBrute = false;
            cmu.InjectOnBurn = false;

            var damage = new DamageSpecifier
            {
                DamageDict =
                {
                    ["Blunt"] = 220,
                },
            };
            SEntMan.System<DamageableSystem>().TryChangeDamage(patient, damage, ignoreResistances: true);
            Assert.That(SComp<MobStateComponent>(patient).CurrentState, Is.EqualTo(MobState.Critical));

            var medibot = SComp<MedibotComponent>(bot);
            Assert.That(SEntMan.System<MedibotSystem>().TryInject((bot, medibot), patient), Is.True,
                "A critical patient must be injected when brute/burn triggers are disabled.");
        });
    }

    [Test]
    public async Task MedibotCanStartCPROnIntactCorpse()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords);
            var corpse = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(1, 0)));
            SEntMan.EnsureComponent<NpcFactionMemberComponent>(corpse).Factions.Add("AUWeYu");

            var damage = new DamageSpecifier
            {
                DamageDict =
                {
                    ["Blunt"] = 1000,
                },
            };
            SEntMan.System<DamageableSystem>().TryChangeDamage(corpse, damage, ignoreResistances: true);

            Assert.That(SComp<MobStateComponent>(corpse).CurrentState, Is.EqualTo(MobState.Dead));
            Assert.That(SEntMan.System<CPRSystem>().TryStartCPR(bot, corpse), Is.True,
                "The medical bot must be able to start CPR on an intact Weyland-Yutani corpse.");
        });
    }

    [Test]
    public async Task MedibotBeakerRequiresOpenServicePanel()
    {
        var map = await Pair.CreateTestMap();
        EntityUid user = default;
        EntityUid bot = default;
        EntityUid beaker = default;
        EntityUid screwdriver = default;

        await Server.WaitAssertion(() =>
        {
            user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(1, 0)));
            bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords);
            beaker = SEntMan.SpawnEntity("Beaker", map.GridCoords.Offset(new(2, 0)));
            screwdriver = SEntMan.SpawnEntity("Screwdriver", map.GridCoords.Offset(new(3, 0)));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(user, screwdriver), Is.True);

            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(
                bot,
                "cmu-medibot-beaker",
                beaker,
                user), Is.False, "A closed service panel must keep the beaker slot locked.");
        });

        await Server.WaitAssertion(() =>
        {
            var interaction = new InteractUsingEvent(
                user,
                screwdriver,
                bot,
                SEntMan.GetComponent<TransformComponent>(bot).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(bot, interaction);
            Assert.That(interaction.Handled, Is.True, "A screwdriver must begin opening the service panel.");
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var panel = SComp<ToolOpenableComponent>(bot);
            Assert.That(panel.IsOpen, Is.True, "The screwdriver must open the service panel.");

            var examine = new FormattedMessage();
            var examined = new ExaminedEvent(examine, bot, user, true, true);
            SEntMan.EventBus.RaiseLocalEvent(bot, examined);
            Assert.That(examined.GetTotalMessage().ToString(), Does.Contain(
                    Loc.GetString("tool-openable-component-examine-opened",
                        ("name", Loc.GetString("cmu-medibot-panel")))),
                "The examine text must report that the service panel is open.");

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(bot, "cmu-medibot-beaker", beaker, user), Is.True,
                "An open service panel must allow inserting a beaker.");
            Assert.That(slots.TryEject(bot, "cmu-medibot-beaker", user, out var ejected), Is.True,
                "An open service panel must allow removing the beaker.");
            Assert.That(ejected, Is.EqualTo(beaker));
        });

        await Server.WaitAssertion(() =>
        {
            var interaction = new InteractUsingEvent(
                user,
                screwdriver,
                bot,
                SEntMan.GetComponent<TransformComponent>(bot).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(bot, interaction);
            Assert.That(interaction.Handled, Is.True, "A screwdriver must begin closing the service panel.");
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var panel = SComp<ToolOpenableComponent>(bot);
            Assert.That(panel.IsOpen, Is.False, "The screwdriver must close the service panel again.");
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(
                bot,
                "cmu-medibot-beaker",
                beaker,
                user), Is.False, "A closed service panel must lock the beaker slot again.");
        });
    }

    [Test]
    public async Task MedibotHandsCannotHoldItems()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var bot = SEntMan.SpawnEntity("CMUMedibot", map.GridCoords);
            var item = SEntMan.SpawnEntity("Screwdriver", map.GridCoords.Offset(new(1, 0)));
            var hands = SEntMan.System<SharedHandsSystem>();
            var handId = SComp<HandsComponent>(bot).Hands.Keys.First();

            Assert.That(hands.TryPickupAnyHand(bot, item), Is.False,
                "The medical bot must not pick up items into its hands.");
            Assert.That(hands.TryForcePickupAnyHand(bot, item), Is.False,
                "The medical bot must not force items into its hands.");

            var containers = SEntMan.System<SharedContainerSystem>();
            var handContainer = containers.GetContainer(bot, handId);
            Assert.That(containers.CanInsert(item, handContainer), Is.False,
                "Nothing may be inserted into the medical bot's hands.");
            Assert.That(containers.Insert(item, handContainer), Is.False,
                "Direct insertion into the medical bot's hands must fail.");
        });
    }

    [Test]
    public async Task CleanbotRemovesBloodDecal()
    {
        var map = await Pair.CreateTestMap();
        DecalIndex blood = default;
        DecalIndex footprint = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.SpawnEntity("CMUCleanBot", map.GridCoords);
            var decals = SEntMan.System<DecalSystem>();
            Assert.That(decals.TryAddDecal(
                "CMUBloodDroplet1",
                map.GridCoords,
                out blood,
                cleanable: true), Is.True);
            Assert.That(decals.TryAddDecal(
                "FootprintShoes",
                map.GridCoords,
                out footprint,
                cleanable: true), Is.True);
        });

        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            var decals = SEntMan.System<DecalSystem>();
            Assert.That(decals.ContainsDecal(map.Grid.Owner, blood), Is.False,
                "The cleaner bot must walk to and remove cleanable blood decals.");
            Assert.That(decals.ContainsDecal(map.Grid.Owner, footprint), Is.False,
                "The cleaner bot must also remove bloody boot footprint decals.");
        });
    }

    private void OpenMedibotPanel(EntityUid bot)
    {
        SComp<ToolOpenableComponent>(bot).IsOpen = true;
        SEntMan.System<ItemSlotsSystem>().SetLock(bot, "cmu-medibot-beaker", false);
    }

    private void InsertTricordBeaker(
        EntityUid bot,
        EntityUid user,
        EntityCoordinates coordinates,
        string prototype = "Beaker")
    {
        var beaker = SEntMan.SpawnEntity(prototype, coordinates);
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        Assert.That(solutions.TryGetSolution(beaker, "beaker", out var beakerSolution, out _), Is.True);
        solutions.TryAddReagent(beakerSolution!.Value, "CMTricordrazine", FixedPoint2.New(60), out _);

        Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(bot, "cmu-medibot-beaker", beaker, user), Is.True,
            "Any dispenser-compatible beaker must fit into the medical bot.");
    }
}
