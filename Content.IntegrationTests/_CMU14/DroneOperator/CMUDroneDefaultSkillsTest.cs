using Content.IntegrationTests.Fixtures;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared._RMC14.Marines.Skills;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.CMU14.DroneOperator;

[TestFixture]
public sealed class CMUDroneDefaultSkillsTest : GameTest
{
    [Test]
    public async Task CivilianDronesHaveNoDefaultSkillsAndPoliceModuleTrainsCqc()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var baseDrone = SEntMan.SpawnEntity("CMUDroneAndroid", map.GridCoords);
            var unarmed = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords.Offset(new(1, 0)));
            var combat = SEntMan.SpawnEntity("CMUDroneAndroidCombat", map.GridCoords.Offset(new(2, 0)));

            Assert.That(SComp<SkillsComponent>(baseDrone).Skills, Is.Empty,
                "The base drone must start without inherited Working Joe skills.");
            Assert.That(SComp<SkillsComponent>(unarmed).Skills, Is.Empty,
                "The civilian drone must start without inherited Working Joe skills.");
            Assert.That(SComp<SkillsComponent>(combat).Skills, Is.Not.Empty,
                "The combat frame keeps its explicit weapon skills.");
            Assert.That(SComp<CMUDroneAndroidComponent>(baseDrone).InheritsOperatorSkills, Is.False);
            Assert.That(SComp<CMUDroneAndroidComponent>(unarmed).InheritsOperatorSkills, Is.False);
            Assert.That(SComp<CMUDroneAndroidComponent>(combat).InheritsOperatorSkills, Is.True);

            var prototype = SProtoMan.Index<EntityPrototype>("CMUDroneModulePolice");
            Assert.That(prototype.TryGetComponent<CMUDroneModuleComponent>(
                out var module,
                Server.ResolveDependency<IComponentFactory>()), Is.True);

            Assert.That(module!.Skills.ContainsKey("RMCSkillCqc"), Is.True,
                "The police module must grant hand-to-hand combat.");
            Assert.That(module.Skills["RMCSkillCqc"], Is.EqualTo(4));
        });
    }
}
