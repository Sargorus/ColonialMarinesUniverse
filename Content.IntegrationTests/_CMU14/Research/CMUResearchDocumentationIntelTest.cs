using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Research;
using Content.Shared._RMC14.Intel;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._CMU14.Research;

[TestFixture]
public sealed class CMUResearchDocumentationIntelTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Destructive = true };

    [TestPrototypes]
    private const string Prototypes = """
        - type: reagent
          parent: Water
          id: CMUTestDocumentIntelContract
          class: Special
          reward: 5
          flags: NoGeneration
        """;

    private const string ContractReagent = "CMUTestDocumentIntelContract";

    private EntityUid SpawnUser(EntityCoordinates coordinates, params string[] facts)
    {
        var user = SEntMan.SpawnEntity("CMMobHuman", coordinates);
        var knowledge = SEntMan.AddComponent<ResearchKnowledgeComponent>(user);
        foreach (var fact in facts)
            knowledge.Facts.Add(fact);
        return user;
    }

    [Test]
    public async Task GovforOwnChemistryGivesNoIntelButWitnessDoes()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var intel = SEntMan.System<IntelSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var fact = documents.RecordContractFact("govfor", proto);
            var user = SpawnUser(map.GridCoords, fact.Id);
            var console = SEntMan.SpawnEntity("RMCComputerIntelGovfor", map.GridCoords);
            var tree = intel.EnsureTechTree("govfor");
            var before = tree.Comp.Tree.Points;

            var result = documents.SubmitIntelKnowledge(user, console, [fact.Id]);
            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.EqualTo(1));
                Assert.That(
                    result.IntelPoints,
                    Is.Zero,
                    "GOVFOR must not convert its own chemistry research into intel.");
                Assert.That(tree.Comp.Tree.Points, Is.EqualTo(before));
            });

            var witness = documents.RecordWitnessFact(
                "xeno:sighting:CMUTestGovforWitness",
                ServerResearchDocumentSystem.ResearchFactKind.XenoSighting,
                "Test drone",
                "A test drone was observed.",
                5,
                0.15f);
            var witnessUser = SpawnUser(map.GridCoords, witness.Id);
            var witnessResult = documents.SubmitIntelKnowledge(witnessUser, console, [witness.Id]);
            Assert.Multiple(() =>
            {
                Assert.That(witnessResult.Accepted, Is.EqualTo(1));
                Assert.That(witnessResult.IntelPoints, Is.GreaterThan(0));
                Assert.That(tree.Comp.Tree.Points, Is.GreaterThan(before));
            });
        });
    }
}
