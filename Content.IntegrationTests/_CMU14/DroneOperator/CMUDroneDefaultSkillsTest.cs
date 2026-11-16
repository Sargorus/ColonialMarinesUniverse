using Content.IntegrationTests.Fixtures;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.Examine;
using Content.Shared._RMC14.Marines.Skills;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

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

    [Test]
    public async Task FrameExamineShowsInsertedPartsAndNextTool()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            var frame = SEntMan.SpawnEntity("CMUDroneBodyFrameUnarmed", map.GridCoords.Offset(new(1, 0)));
            var comp = SEntMan.GetComponent<CMUDroneFrameComponent>(frame);

            var framePrototype = SProtoMan.Index<EntityPrototype>("CMUDroneBodyFrameUnarmed");
            Assert.That(framePrototype.TryGetComponent<CMUDroneFrameComponent>(
                out var frameComponent,
                Server.ResolveDependency<IComponentFactory>()), Is.True);
            Assert.That(frameComponent!.DronePrototype.Id, Is.EqualTo("CMUUtilityDrone"),
                "the civilian frame assembles into a programmable utility drone chassis");

            var text = Examine(frame, user);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("cmu-drone-frame-examine-title")));
                Assert.That(text, Does.Contain(Loc.GetString("cmu-drone-frame-examine-missing")));
                Assert.That(text, Does.Contain(Loc.GetString("cmu-drone-frame-open-tool-required")));
            });

            comp.PortsOpen = true;
            foreach (var part in comp.RequiredParts)
                comp.PartStates[part] = CMUDroneAssemblyPartState.Installed;
            text = Examine(frame, user);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("cmu-drone-frame-examine-installed")));
                Assert.That(text, Does.Contain(Loc.GetString(
                    "cmu-drone-frame-clamp-tool-required",
                    ("part", Loc.GetString("cmu-drone-frame-part-head")))));
            });

            foreach (var part in comp.RequiredParts)
                comp.PartStates[part] = CMUDroneAssemblyPartState.Clamped;
            text = Examine(frame, user);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("cmu-drone-frame-examine-clamped")));
                Assert.That(text, Does.Contain(Loc.GetString(
                    "cmu-drone-frame-weld-tool-required",
                    ("part", Loc.GetString("cmu-drone-frame-part-head")))));
            });

            foreach (var part in comp.RequiredParts)
                comp.PartStates[part] = CMUDroneAssemblyPartState.Welded;
            text = Examine(frame, user);
            Assert.That(text, Does.Contain(Loc.GetString("cmu-drone-frame-ready-key")));
        });
    }

    private string Examine(EntityUid frame, EntityUid user)
    {
        var examine = new ExaminedEvent(new FormattedMessage(), frame, user, true, false);
        SEntMan.EventBus.RaiseLocalEvent(frame, examine);
        return examine.GetTotalMessage().ToString();
    }
}
