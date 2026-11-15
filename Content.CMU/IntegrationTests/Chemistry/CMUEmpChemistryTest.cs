using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.FixedPoint;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Prototypes;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.CMU14.Chemistry;

[TestFixture]
public sealed class CMUEmpChemistryTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task EmpReactionDrainsBatteryAndResidueIsNotAContractChemical()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var beaker = SEntMan.SpawnEntity("Beaker", map.GridCoords);
            var cell = SEntMan.SpawnEntity("PowerCellMedium", map.GridCoords);
            var solutions = Server.System<SharedSolutionContainerSystem>();
            var reactions = Server.System<ChemicalReactionSystem>();
            var batteries = Server.System<SharedBatterySystem>();

            Assert.That(solutions.TryGetFitsInDispenser(beaker, out var beakerSolution, out _), Is.True);
            var solution = beakerSolution!.Value;
            Assert.That(solutions.TryAddReagent(solution, "RMCSilver", FixedPoint2.New(10)), Is.True);
            Assert.That(solutions.TryAddReagent(solution, "RMCUranium", FixedPoint2.New(10)), Is.True);
            Assert.That(solutions.TryAddReagent(solution, "RMCPhoron", FixedPoint2.New(10)), Is.True);

            var mixer = SEntMan.EnsureComponent<ReactionMixerComponent>(beaker);
            mixer.ReactionTypes = ["CMUMix"];
            mixer.MixerType = ReactionMixerType.None;

            batteries.SetCharge(cell, 5000f);
            var before = batteries.GetCharge(cell);

            reactions.FullyReactSolution(solution, mixer);

            Assert.That(batteries.GetCharge(cell), Is.LessThan(before),
                "the EMP reaction must drain a nearby battery");
            Assert.That(solution.Comp.Solution.GetTotalPrototypeQuantity("CMUEmpResidue"),
                Is.GreaterThan(FixedPoint2.Zero),
                "the reaction must leave its inert residue");

            var generator = Server.System<ServerReagentGeneratorSystem>();
            Assert.That(generator.ChemicalGenClassesList.SelectMany(pair => pair.Value),
                Does.Not.Contain("CMUEmpResidue"),
                "the EMP residue must never be selected for a chemical contract");

            var contract = new GeneratedReagentData
            {
                Recipe = new()
                {
                    ["RMCSilver"] = (2, false),
                    ["RMCUranium"] = (1, false),
                    ["RMCPhoron"] = (2, false),
                },
            };
            Assert.That(generator.IsDuplicate(ref contract, out _), Is.True,
                "a contract recipe containing the EMP blend must be rejected as a duplicate reaction");
        });
    }

    [Test]
    public async Task EmpPayloadWorksInModularGrenadeAndMine()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var battery = SEntMan.SpawnEntity("PowerCellMedium", map.GridCoords);
            var grenade = SEntMan.SpawnEntity("ModularGrenade", map.GridCoords);
            var mine = SEntMan.SpawnEntity("LandMineModular", map.GridCoords);
            var grenadePayload = SEntMan.SpawnEntity("EmpPayload", map.GridCoords);
            var minePayload = SEntMan.SpawnEntity("EmpPayload", map.GridCoords);

            var containers = Server.System<SharedContainerSystem>();
            var triggers = Server.System<TriggerSystem>();
            var batteries = Server.System<SharedBatterySystem>();

            Assert.That(SProtoMan.HasIndex<LatheRecipePrototype>("CMUEmpPayload"), Is.True,
                "the EMP payload must have a lathe recipe");

            Assert.That(containers.Insert(grenadePayload,
                containers.EnsureContainer<Container>(grenade, "payload")), Is.True);
            Assert.That(containers.Insert(minePayload,
                containers.EnsureContainer<Container>(mine, "payload")), Is.True);

            batteries.SetCharge(battery, 5000f);
            var before = batteries.GetCharge(battery);
            triggers.Trigger(grenade, key: "trigger");
            Assert.That(batteries.GetCharge(battery), Is.LessThan(before),
                "an EMP payload must fire from a modular grenade");

            batteries.SetCharge(battery, 5000f);
            before = batteries.GetCharge(battery);
            triggers.Trigger(mine);
            Assert.That(batteries.GetCharge(battery), Is.LessThan(before),
                "an EMP payload must fire from a modular mine");
        });
    }
}
