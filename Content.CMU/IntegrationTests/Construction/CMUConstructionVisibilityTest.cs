using Content.IntegrationTests.Fixtures;
using Content.Shared.Construction.Prototypes;

namespace Content.IntegrationTests.CMU14.Construction;

[TestFixture]
public sealed class CMUConstructionVisibilityTest : GameTest
{
    [Test]
    public void SurveillanceAndSignalPartsAreVisibleInCMConstructionMenu()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SProtoMan.Index<ConstructionPrototype>("camera").IsCM, Is.True,
                "the constructible camera must be visible in the CM construction menu");
            Assert.That(SProtoMan.Index<ConstructionPrototype>("WallmountTelescreen").IsCM, Is.True,
                "the wall camera monitor must be visible in the CM construction menu");
            Assert.That(SProtoMan.Index<ConstructionPrototype>("SignalSwitchRecipe").IsCM, Is.True,
                "the signal switch must be visible in the CM construction menu");
            Assert.That(SProtoMan.Index<ConstructionPrototype>("SignalButtonRecipe").IsCM, Is.True,
                "the signal button must be visible in the CM construction menu");
            Assert.That(SProtoMan.Index<ConstructionPrototype>("CMUSignalGasVentRecipe").IsCM, Is.True,
                "the signal gas vent must be visible in the CM construction menu");
        });
    }
}
