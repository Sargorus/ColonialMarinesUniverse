using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMUDroneCargoTest : GameTest
{
    [Test]
    public async Task DroneOutfitsUseCmuGear()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = Server.ResolveDependency<IComponentFactory>();

            var engineering = SProtoMan.Index<EntityPrototype>("CMUCrateDroneOutfitEngineering");
            Assert.That(engineering.TryComp<StorageFillComponent>(out var engineeringFill, factory), Is.True);
            Assert.That(engineeringFill.Contents.Select(entry => entry.PrototypeId?.Id),
                Does.Contain("CMBackpackWelder"));
            Assert.That(engineeringFill.Contents.Select(entry => entry.PrototypeId?.Id),
                Does.Not.Contain("RMCBackpackWelderPMC"));

            var medical = SProtoMan.Index<EntityPrototype>("CMUCrateDroneOutfitMedical");
            Assert.That(medical.TryComp<StorageFillComponent>(out var medicalFill, factory), Is.True);
            Assert.That(medicalFill.Contents.Select(entry => entry.PrototypeId?.Id),
                Does.Contain("RMCLabcoat"));
            Assert.That(medicalFill.Contents.Select(entry => entry.PrototypeId?.Id),
                Does.Not.Contain("ClothingOuterCoatLab"));

            var catalog = SProtoMan.Index<EntityPrototype>("AU14CorporateASRSConsole");
            Assert.That(catalog.TryComp<RequisitionsComputerComponent>(out var requisitions, factory), Is.True);
            var droneCategory = requisitions.Categories.Single(category => category.Name == "WEY drone");
            Assert.That(droneCategory.Entries.Single(entry => entry.Crate.Id == "CMUCrateDroneOutfitEngineering").Cost,
                Is.GreaterThan(0));
            Assert.That(droneCategory.Entries.Single(entry => entry.Crate.Id == "CMUCrateDroneOutfitMedical").Cost,
                Is.GreaterThan(0));
            Assert.That(droneCategory.Entries.Single(entry => entry.Crate.Id == "CMUCrateDroneOutfitPolice").Cost,
                Is.GreaterThan(0));
        });
    }
}
