using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Research;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared._RMC14.Xenonids.Parasite;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests._CMU14.Research;

[TestFixture]
public sealed class CMUResearchDocumentInteractionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Destructive = true };

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

    [Test]
    public async Task WitnessFactCapsAtThreeSubmissionsAndOnePerPlayer()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ServerResearchDataTerminalSystem>();
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var fact = documents.RecordWitnessFact(
                "xeno:sighting:CMUTestWitness",
                ServerResearchDocumentSystem.ResearchFactKind.XenoSighting,
                "Test drone",
                "A test drone was observed.",
                5,
                0.15f);
            var corporateTerminal = SEntMan.SpawnEntity("CMUResearchDataTerminalCorporate", map.GridCoords);
            var opforTerminal = SEntMan.SpawnEntity("CMUResearchDataTerminalOpfor", map.GridCoords);
            var govforTerminal = SEntMan.SpawnEntity("CMUResearchDataTerminalGovfor", map.GridCoords);
            var colonyTerminal = SEntMan.SpawnEntity("CMUResearchDataTerminalColony", map.GridCoords);
            foreach (var faction in new[] { "corporate", "opfor", "govfor", "colony" })
                research.UpdateClearance(0, -1, faction, 0);
            var users = new[]
            {
                SpawnUser(map.GridCoords, fact.Id),
                SpawnUser(map.GridCoords, fact.Id),
                SpawnUser(map.GridCoords, fact.Id),
                SpawnUser(map.GridCoords, fact.Id),
            };
            var terminals = new[] { corporateTerminal, opforTerminal, govforTerminal, colonyTerminal };
            for (var i = 0; i < 3; i++)
            {
                var result = documents.SubmitKnowledge(users[i], terminals[i], [fact.Id]);
                Assert.That(result.Accepted, Is.EqualTo(1), $"witness submission {i + 1} must be paid");
            }
            var fourth = documents.SubmitKnowledge(users[3], colonyTerminal, [fact.Id]);
            Assert.That(fourth.Accepted, Is.Zero, "the fourth witness submission must be rejected");

            var secondFact = documents.RecordWitnessFact(
                "xeno:sighting:CMUTestWitnessTwo",
                ServerResearchDocumentSystem.ResearchFactKind.XenoSighting,
                "Second test drone",
                "Another test drone was observed.",
                5,
                0.15f);
            SEntMan.GetComponent<ResearchKnowledgeComponent>(users[0]).Facts.Add(secondFact.Id);
            Assert.That(documents.SubmitKnowledge(users[0], corporateTerminal, [secondFact.Id]).Accepted,
                Is.EqualTo(1));
            var playerDuplicate = documents.SubmitKnowledge(users[0], opforTerminal, [secondFact.Id]);
            Assert.That(playerDuplicate.Accepted, Is.Zero,
                "one player must not claim the same witness fact through two factions");
        });
    }

    [Test]
    public async Task ExaminingXenoGrantsWitnessFact()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var user = SpawnUser(map.GridCoords);
            var xeno = SEntMan.SpawnEntity("CMXenoDrone", map.GridCoords.Offset(new(1, 0)));
            var examined = new ExaminedEvent(new FormattedMessage(), xeno, user, true, false);
            SEntMan.EventBus.RaiseLocalEvent(xeno, examined);
            const string factId = "xeno:sighting:CMXenoDrone";
            Assert.That(
                SEntMan.GetComponent<ResearchKnowledgeComponent>(user).Facts,
                Does.Contain(factId));
            var fact = documents.GetFactForTest(factId);
            Assert.That(fact, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(fact!.SampleData, Is.EqualTo(5));
                Assert.That(fact.MaxSubmissions, Is.EqualTo(3));
            });
        });
    }

    [Test]
    public async Task ChestburstGrantsKnowledgeToNearbyObserver()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var user = SpawnUser(map.GridCoords);
            var victim = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(1, 0)));
            SEntMan.AddComponent<VictimBurstComponent>(victim);
            const string factId = "xeno:burst";
            Assert.That(
                SEntMan.GetComponent<ResearchKnowledgeComponent>(user).Facts,
                Does.Contain(factId));
            var fact = documents.GetFactForTest(factId);
            Assert.That(fact, Is.Not.Null);
            Assert.That(fact!.SampleData, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task RmcPaperAndPenOfferWriteVerb()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            var fact = documents.RecordWitnessFact(
                "xeno:sighting:CMUTestRmcWrite",
                ServerResearchDocumentSystem.ResearchFactKind.XenoSighting,
                "Test drone",
                "A test drone was observed.",
                5,
                0.15f);
            var user = SpawnUser(map.GridCoords, fact.Id);
            var pen = SEntMan.SpawnEntity("CMPen", map.GridCoords);
            var paper = SEntMan.SpawnEntity("CMPaper", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, pen), Is.True);
            var userHands = SEntMan.GetComponent<HandsComponent>(user);
            var verbs = new GetVerbsEvent<AlternativeVerb>(
                user,
                paper,
                pen,
                userHands,
                canInteract: true,
                canComplexInteract: true,
                canAccess: true,
                extraCategories: []);
            SEntMan.EventBus.RaiseLocalEvent(paper, verbs);
            Assert.That(
                verbs.Verbs.Any(verb => verb.Text == Loc.GetString("cmu-research-documents-write-verb")),
                Is.True);
        });
    }

    [Test]
    public async Task RememberVerbEnabledWithoutKnowledge()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var fact = documents.RecordWitnessFact(
                "xeno:sighting:CMUTestRememberVerb",
                ServerResearchDocumentSystem.ResearchFactKind.XenoSighting,
                "Test drone",
                "A test drone was observed.",
                5,
                0.15f);
            var user = SpawnUser(map.GridCoords);
            var paper = SEntMan.SpawnEntity("CMPaper", map.GridCoords);
            var document = SEntMan.AddComponent<ResearchDocumentComponent>(paper);
            document.FactIds = [fact.Id];
            document.ScannerReport = true;
            var verbs = new GetVerbsEvent<AlternativeVerb>(
                user,
                paper,
                null,
                SEntMan.GetComponent<HandsComponent>(user),
                canInteract: true,
                canComplexInteract: true,
                canAccess: true,
                extraCategories: []);
            SEntMan.EventBus.RaiseLocalEvent(paper, verbs);
            var remember = verbs.Verbs.Single(
                verb => verb.Text == Loc.GetString("cmu-research-documents-remember-verb"));
            Assert.That(remember.Disabled, Is.False,
                "a player who knows nothing must still be able to memorize a scanner report");
        });
    }

    [Test]
    public async Task PaperWritingTurnsBlankPaperIntoResearchDocument()
    {
        var map = await Pair.CreateTestMap();
        EntityUid paper = default;
        await Server.WaitAssertion(() =>
        {
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            var fact = documents.RecordWitnessFact(
                "xeno:sighting:CMUTestWrite",
                ServerResearchDocumentSystem.ResearchFactKind.XenoSighting,
                "Test drone",
                "A test drone was observed.",
                5,
                0.15f);
            var user = SpawnUser(map.GridCoords, fact.Id);
            var pen = SEntMan.SpawnEntity("Pen", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, pen), Is.True);
            paper = SEntMan.SpawnEntity("Paper", map.GridCoords);
            var message = new ResearchDocumentWriteMessage(
                "Field report",
                ResearchDocumentType.Record,
                [fact.Id])
            {
                Actor = user,
                UiKey = ResearchDocumentWriteUI.Key,
            };
            SEntMan.EventBus.RaiseLocalEvent(paper, message);
        });
        await Pair.RunTicksSync(Pair.SecondsToTicks(4));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<ResearchDocumentComponent>(paper, out var document), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(document!.ScannerReport, Is.False);
                Assert.That(document.Title, Is.EqualTo("Field report"));
                Assert.That(document.Type, Is.EqualTo(ResearchDocumentType.Record));
                Assert.That(SEntMan.GetComponent<PaperComponent>(paper).EditingDisabled, Is.True);
            });
        });
    }
}
