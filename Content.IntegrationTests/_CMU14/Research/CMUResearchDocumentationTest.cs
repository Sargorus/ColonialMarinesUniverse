using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Research;
using Content.Server.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Paper;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._CMU14.Research;

[TestFixture]
public sealed class CMUResearchDocumentationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Destructive = true };

    [TestPrototypes]
    private const string Prototypes = """
        - type: reagent
          parent: Water
          id: CMUTestDocumentContract
          class: Special
          reward: 5
          flags: NoGeneration
        """;

    private const string ContractReagent = "CMUTestDocumentContract";

    private EntityUid SpawnUser(EntityCoordinates coordinates, params string[] facts)
    {
        var user = SEntMan.SpawnEntity("CMMobHuman", coordinates);
        if (facts.Length == 0)
            return user;
        var knowledge = SEntMan.AddComponent<ResearchKnowledgeComponent>(user);
        foreach (var fact in facts)
            knowledge.Facts.Add(fact);
        return user;
    }

    private EntityUid SpawnScannerReport(EntityCoordinates coordinates, string factId)
    {
        var paper = SEntMan.SpawnEntity("Paper", coordinates);
        var document = SEntMan.AddComponent<ResearchDocumentComponent>(paper);
        document.FactIds = [factId];
        document.ScannerReport = true;
        return paper;
    }

    [Test]
    public async Task RememberAddsKnowledgeWithoutPaying()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ServerResearchDataTerminalSystem>();
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var fact = documents.RecordContractFact("corporate", proto);
            var user = SpawnUser(map.GridCoords);
            var paper = SpawnScannerReport(map.GridCoords, fact.Id);
            var credits = research.GetCredits("corporate");
            var sample = research.GetSampleData("corporate");

            Assert.That(documents.RememberDocument(user, paper), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(
                    SEntMan.GetComponent<ResearchKnowledgeComponent>(user).Facts,
                    Does.Contain(fact.Id));
                Assert.That(research.GetCredits("corporate"), Is.EqualTo(credits));
                Assert.That(research.GetSampleData("corporate"), Is.EqualTo(sample));
            });
            Assert.That(documents.RememberDocument(user, paper), Is.False,
                "Memorizing the same report twice must not add another fact.");
        });
    }

    [Test]
    public async Task ChemistrySubmissionPaysOwnFactOnceAndRejectsDuplicate()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ServerResearchDataTerminalSystem>();
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var fact = documents.RecordContractFact("corporate", proto);
            var user = SpawnUser(map.GridCoords, fact.Id);
            var terminal = SEntMan.SpawnEntity("CMUResearchDataTerminalCorporate", map.GridCoords);
            var console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            var corporate = SEntMan.GetComponent<CorporateConsoleComponent>(console);
            corporate.CorporateBudget = 0;
            research.UpdateClearance(0, -1, "corporate", 0);

            var result = documents.SubmitKnowledge(user, terminal, [fact.Id]);
            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.EqualTo(1));
                Assert.That(research.GetCredits("corporate"), Is.EqualTo(proto.Reward));
                Assert.That(research.GetSampleData("corporate"),
                    Is.EqualTo(ServerResearchDataTerminalSystem.SampleDataForCompleted(proto)));
                Assert.That(corporate.CorporateBudget, Is.EqualTo(fact.Cash));
            });

            var duplicate = documents.SubmitKnowledge(user, terminal, [fact.Id]);
            Assert.Multiple(() =>
            {
                Assert.That(duplicate.Accepted, Is.Zero);
                Assert.That(research.GetCredits("corporate"), Is.EqualTo(proto.Reward));
                Assert.That(corporate.CorporateBudget, Is.EqualTo(fact.Cash));
            });
        });
    }

    [Test]
    public async Task ForeignContractPaysScienceWithoutCash()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ServerResearchDataTerminalSystem>();
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var fact = documents.RecordContractFact("corporate", proto);
            var user = SpawnUser(map.GridCoords, fact.Id);
            var terminal = SEntMan.SpawnEntity("CMUResearchDataTerminalOpfor", map.GridCoords);
            var console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            var corporate = SEntMan.GetComponent<CorporateConsoleComponent>(console);
            corporate.CorporateBudget = 0;
            research.UpdateClearance(0, -1, "opfor", 0);

            var result = documents.SubmitKnowledge(user, terminal, [fact.Id]);
            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.EqualTo(1));
                Assert.That(research.GetCredits("opfor"), Is.EqualTo(proto.Reward));
                Assert.That(research.GetSampleData("opfor"),
                    Is.EqualTo(ServerResearchDataTerminalSystem.SampleDataForCompleted(proto)));
                Assert.That(result.Cash, Is.Zero, "A foreign contract may pay science, not cash.");
                Assert.That(corporate.CorporateBudget, Is.Zero);
            });
        });
    }

    [Test]
    public async Task PrototypesExposeResearchDocumentUis()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            var paper = SEntMan.SpawnEntity("CMPaper", map.GridCoords);
            var terminal = SEntMan.SpawnEntity("CMUResearchDataTerminalCorporate", map.GridCoords);
            var intel = SEntMan.SpawnEntity("RMCComputerIntelGovfor", map.GridCoords);
            var ui = SEntMan.System<Robust.Shared.GameObjects.SharedUserInterfaceSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(ui.HasUi(player, ResearchKnowledgeViewUI.Key), Is.True);
                Assert.That(ui.HasUi(paper, ResearchDocumentWriteUI.Key), Is.True);
                Assert.That(ui.HasUi(terminal, ResearchKnowledgeSubmitUI.Key), Is.True);
                Assert.That(ui.HasUi(intel, ResearchKnowledgeSubmitUI.Key), Is.True);
            });
        });
    }

    [Test]
    public async Task ScannerReportRebuildsFactsFromStoredReport()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var paper = SEntMan.SpawnEntity("Paper", map.GridCoords);
            var document = SEntMan.AddComponent<ResearchDocumentComponent>(paper);
            document.ScannerReport = true;
            document.SourceFaction = "corporate";
            var report = SEntMan.AddComponent<ResearchReportComponent>(paper);
            report.Data = new GeneratedReagentData
            {
                ID = proto.ID,
                Class = ReagentClass.Ultra,
            };
            var count = documents.EnsureScannerReportFacts(paper, document);
            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(1));
                Assert.That(document.FactIds, Does.Contain($"contract:corporate:{proto.ID}"));
            });
        });
    }

    [Test]
    public async Task ScannerReportCarriesContractFact()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var paper = SEntMan.SpawnEntity("Paper", map.GridCoords);
            var report = new ResearchReportData
            {
                Name = "test report",
                Info = "test",
                Completed = true,
                Valid = true,
                Icon = ResearchReportIconEnum.Full,
            };
            var count = documents.AttachScannerReport(paper, "corporate", proto, report, []);
            Assert.That(count, Is.EqualTo(1));
            var document = SEntMan.GetComponent<ResearchDocumentComponent>(paper);
            Assert.Multiple(() =>
            {
                Assert.That(document.ScannerReport, Is.True);
                Assert.That(document.FactIds, Does.Contain($"contract:corporate:{proto.ID}"));
            });
        });
    }
}
