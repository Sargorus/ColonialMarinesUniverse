using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._RMC14.Crate;
using Content.Shared._RMC14.Projectiles;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMURoboticsCrateContentsTest : GameTest
{
    [Test]
    public async Task FabricatorCrateDoesNotSpawnRandomCrateLoot()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var crate = SEntMan.SpawnEntity("CMUCrateRoboticsFabricator", map.GridCoords);

            Assert.That(SEntMan.HasComponent<SpawnOnTerminateComponent>(crate), Is.False,
                "The fabricator crate must not inherit the random secure-case loot spawner.");

            var openable = SComp<CrateOpenableComponent>(crate);
            var prototypes = openable.Spawn
                .Select(entry => entry.PrototypeId?.Id)
                .Where(id => id != null)
                .ToList();
            Assert.That(prototypes, Is.EquivalentTo(new[] { "CMURoboticsFabricator" }),
                "Opening the crate must produce only the fabricator, not a wood plank or random loot.");
        });
    }
}
