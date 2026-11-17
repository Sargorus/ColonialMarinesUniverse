using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CMU14.Chemistry.Research;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests._CMU14.Research;

[TestFixture]
public sealed class CMUFactionResearchTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = """
        - type: reagent
          parent: Water
          id: CMUTestFactionContract
          class: Special
          reward: 5
          flags: NoGeneration
        """;

    private const string ContractReagent = "CMUTestFactionContract";

    private EntityUid SpawnContractUser(EntityCoordinates coordinates, string factId)
    {
        var user = SEntMan.SpawnEntity("CMMobHuman", coordinates);
        var knowledge = SEntMan.AddComponent<ResearchKnowledgeComponent>(user);
        knowledge.Facts.Add(factId);
        knowledge.WitnessedFacts.Add(factId);
        return user;
    }

    private void SetResearchDirection(EntityUid console, EntityUid user, CorporateResearchDirection direction)
    {
        SEntMan.EventBus.RaiseLocalEvent(console, new CorporateConsoleSetDirectionBuiMsg(direction)
        {
            Actor = user,
            UiKey = CorporateConsoleUi.Key,
        });
    }

    [Test]
    public async Task CooldownPurchasesAreIsolatedAndValidated()
    {
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ServerResearchDataTerminalSystem>();
            var now = Server.ResolveDependency<IGameTiming>().CurTime;
            var gov = system.GetResearch("govfor");
            var op = system.GetResearch("opfor");
            var oldCount = system.ResearchChemAmount;
            try
            {
                system.UpdateClearance(2, 3, "govfor");
                system.UpdateClearance(9, 2, "opfor");
                gov.Picked = true;
                gov.NextReroll = now + TimeSpan.FromSeconds(90);
                op.Picked = true;
                op.NextReroll = now + TimeSpan.FromSeconds(360);
                Assert.That(system.ReduceCooldown("govfor"), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(gov.NextReroll, Is.EqualTo(now + TimeSpan.FromSeconds(30)));
                    Assert.That(system.GetCredits("govfor"), Is.EqualTo(1));
                    Assert.That(system.GetClearance("govfor"), Is.EqualTo(3));
                    Assert.That(op.NextReroll, Is.EqualTo(now + TimeSpan.FromSeconds(360)));
                    Assert.That(system.GetCredits("opfor"), Is.EqualTo(9));
                    Assert.That(system.GetClearance("opfor"), Is.EqualTo(2));
                    Assert.That(gov.Reports, Is.Not.SameAs(op.Reports));
                    Assert.That(gov.Selectable, Is.Not.SameAs(op.Selectable));
                });

                // Isolate timer completion from random chemical generation.
                system.ResearchChemAmount = 0;
                Assert.That(system.ReduceCooldown("govfor"), Is.True);
                Assert.That(gov.Picked, Is.False);
                Assert.That(gov.NextReroll, Is.EqualTo(now + system.RerollTime));
                Assert.That(system.GetCredits("govfor"), Is.Zero);
                Assert.That(system.ReduceCooldown("govfor"), Is.False);

                gov.Picked = true;
                Assert.That(system.ReduceCooldown("govfor"), Is.False, "No points must not buy time.");
                system.UpdateClearance(2, -1, "govfor");
                gov.NextReroll = now;
                Assert.That(system.ReduceCooldown("govfor"), Is.False, "Expired waits must not charge points.");
                gov.Picked = false;
                gov.NextReroll = now + TimeSpan.FromSeconds(90);
                Assert.That(system.ReduceCooldown("govfor"), Is.False, "Unpicked offers must not charge points.");
                Assert.That(system.GetCredits("govfor"), Is.EqualTo(2));

                system.PickChem("invalid-contract-id");
                Assert.That(system.GetResearch("corporate").Picked, Is.False);
            }
            finally
            {
                system.ResearchChemAmount = oldCount;
                system.UpdateClearance(0, 1, "govfor");
                system.UpdateClearance(0, 1, "opfor");
                gov.Picked = op.Picked = false;
                gov.NextReroll = op.NextReroll = TimeSpan.Zero;
            }
        });
    }

    [Test]
    public async Task RoboticsDirectionMultipliesContractCredits()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ServerResearchDataTerminalSystem>();
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var fact = documents.RecordContractFact("corporate", proto);
            var user = SpawnContractUser(map.GridCoords, fact.Id);
            var console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            var terminal = SEntMan.SpawnEntity("CMUResearchDataTerminalCorporate", map.GridCoords);
            SetResearchDirection(console, user, CorporateResearchDirection.Robotics);
            system.UpdateClearance(0, -1, "corporate", 0);

            var result = documents.SubmitKnowledge(user, terminal, [fact.Id]);
            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.EqualTo(1));
                Assert.That(result.Credits, Is.EqualTo(8),
                    "robotics pays 1.5x contract credits rounded up");
                Assert.That(system.GetCredits("corporate"), Is.EqualTo(8));
            });
            system.UpdateClearance(0, -1, "corporate", 0);
        });
    }

    [Test]
    public async Task BioengineeringDirectionAddsOneContractCredit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ServerResearchDataTerminalSystem>();
            var documents = SEntMan.System<ServerResearchDocumentSystem>();
            var proto = SProtoMan.Index<ReagentPrototype>(ContractReagent);
            var fact = documents.RecordContractFact("corporate", proto);
            var user = SpawnContractUser(map.GridCoords, fact.Id);
            var console = SEntMan.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            var terminal = SEntMan.SpawnEntity("CMUResearchDataTerminalCorporate", map.GridCoords);
            SetResearchDirection(console, user, CorporateResearchDirection.Bioengineering);
            system.UpdateClearance(0, -1, "corporate", 0);

            var result = documents.SubmitKnowledge(user, terminal, [fact.Id]);
            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.EqualTo(1));
                Assert.That(result.Credits, Is.EqualTo(6),
                    "bioengineering pays one extra chemistry contract credit");
                Assert.That(system.GetCredits("corporate"), Is.EqualTo(6));
            });
            system.UpdateClearance(0, -1, "corporate", 0);
        });
    }

    [Test]
    public async Task LiaisonShowsChemistryCreditsNotSampleData()
    {
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ServerResearchDataTerminalSystem>();
            system.UpdateClearance(7, -1, "corporate", 3);
            var query = new CMUScienceStatusQueryEvent("corporate");
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref query);
            Assert.Multiple(() =>
            {
                Assert.That(query.ChemistryPoints, Is.EqualTo(7),
                    "the liaison console shows chemistry credits, not sample data");
                Assert.That(system.GetSampleData("corporate"), Is.EqualTo(3));
            });
            system.UpdateClearance(0, -1, "corporate", 0);
        });
    }

    [Test]
    public async Task GuaranteedPropertyLeadsNextContract()
    {
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ServerResearchDataTerminalSystem>();
            var research = system.GetResearch("corporate");
            var terminal = SEntMan.SpawnEntity(
                "CMUResearchDataTerminalCorporate",
                MapCoordinates.Nullspace);

            try
            {
                system.UpdateClearance(20, -1, "corporate", 7);
                research.Selectable.Clear();
                research.Selectable.Add(new GeneratedReagentData
                {
                    ID = "guaranteed-contract",
                    Effects = new() { ["Toxic"] = 1 },
                });

                SEntMan.EventBus.RaiseLocalEvent(terminal, new ResearchDataTerminalBuyGuaranteeBuiMsg("Boosting")
                {
                    Actor = EntityUid.Invalid,
                    UiKey = ResearchDataTerminalUI.Key,
                });

                Assert.Multiple(() =>
                {
                    Assert.That(system.GetCredits("corporate"), Is.EqualTo(5),
                        "a legendary guarantee costs 15 research credits");
                    Assert.That(system.GetSampleData("corporate"), Is.EqualTo(7),
                        "guarantees must not spend sample data");
                    Assert.That(research.GuaranteedProperty, Is.Null);
                    Assert.That(research.Selectable[0].GenTier, Is.EqualTo(3),
                        "a legendary property requires a hard contract");
                    Assert.That(research.Selectable[0].Effects.Keys.First(), Is.EqualTo("Boosting"));
                });
            }
            finally
            {
                system.UpdateClearance(0, -1, "corporate", 0);
                research.Selectable.Clear();
                research.GuaranteedProperty = null;
                research.GuaranteedPropertyCost = 0;
                SEntMan.DeleteEntity(terminal);
            }
        });
    }

    [Test]
    public async Task GuaranteeTierFollowsPropertyRarity()
    {
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ServerResearchDataTerminalSystem>();
            var research = system.GetResearch("corporate");
            var terminal = SEntMan.SpawnEntity(
                "CMUResearchDataTerminalCorporate",
                MapCoordinates.Nullspace);

            try
            {
                system.UpdateClearance(40, -1, "corporate", 7);
                research.Selectable.Clear();
                research.Selectable.Add(new GeneratedReagentData
                {
                    ID = "guarantee-tier-contract",
                    Effects = new() { ["Toxic"] = 1 },
                });

                SEntMan.EventBus.RaiseLocalEvent(terminal, new ResearchDataTerminalBuyGuaranteeBuiMsg("Toxic")
                {
                    Actor = EntityUid.Invalid,
                    UiKey = ResearchDataTerminalUI.Key,
                });
                Assert.Multiple(() =>
                {
                    Assert.That(research.Selectable[0].GenTier, Is.EqualTo(1),
                        "common properties require an easy contract");
                    Assert.That(research.Selectable[0].Effects.Keys.First(), Is.EqualTo("Toxic"));
                    Assert.That(system.GetCredits("corporate"), Is.EqualTo(32));
                    Assert.That(system.GetSampleData("corporate"), Is.EqualTo(7));
                });

                research.Selectable.Clear();
                research.Selectable.Add(new GeneratedReagentData
                {
                    ID = "guarantee-rare-contract",
                    Effects = new() { ["Toxic"] = 1 },
                });
                SEntMan.EventBus.RaiseLocalEvent(terminal, new ResearchDataTerminalBuyGuaranteeBuiMsg("Addictive")
                {
                    Actor = EntityUid.Invalid,
                    UiKey = ResearchDataTerminalUI.Key,
                });
                Assert.Multiple(() =>
                {
                    Assert.That(research.Selectable[0].GenTier, Is.EqualTo(2),
                        "rare properties require an intermediate contract");
                    Assert.That(research.Selectable[0].Effects.Keys.First(), Is.EqualTo("Addictive"));
                    Assert.That(system.GetCredits("corporate"), Is.EqualTo(24));
                });
            }
            finally
            {
                system.UpdateClearance(0, -1, "corporate", 0);
                research.Selectable.Clear();
                research.GuaranteedProperty = null;
                research.GuaranteedPropertyCost = 0;
                SEntMan.DeleteEntity(terminal);
            }
        });
    }

    [Test]
    public async Task AcceptingContractLocksEveryTerminalInFaction()
    {
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ServerResearchDataTerminalSystem>();
            var gov = system.GetResearch("govfor");
            var op = system.GetResearch("opfor");
            gov.Selectable.Add(new GeneratedReagentData { ID = "gov-contract-one" });
            gov.Selectable.Add(new GeneratedReagentData { ID = "gov-contract-two" });
            op.Selectable.Add(new GeneratedReagentData { ID = "op-contract" });

            try
            {
                Assert.That(system.TryReserveContract("govfor", "gov-contract-one", out var accepted), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(accepted.ID, Is.EqualTo("gov-contract-one"));
                    Assert.That(gov.Picked, Is.True);
                    Assert.That(system.TryReserveContract("govfor", "gov-contract-two", out _), Is.False,
                        "A stale window from another GOVFOR terminal must not accept another contract.");
                    Assert.That(system.TryReserveContract("opfor", "op-contract", out _), Is.True,
                        "A different faction's contract pool must remain available.");
                });
            }
            finally
            {
                gov.Selectable.Clear();
                op.Selectable.Clear();
                gov.Picked = op.Picked = false;
                gov.NextReroll = op.NextReroll = TimeSpan.Zero;
            }
        });
    }

}
