using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.CMU14.Requisitions;
using Content.Shared.Roles;
using Content.Shared.Storage.Components;
using Content.Shared._RMC14.Attachable.Components;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared._RMC14.Weapons.Ranged;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.CMU14.Requisitions;

[TestFixture]
public sealed class CorporateSecurityCargoTest : GameTest
{
    [Test]
    public async Task SecurityCargoIsSoldByCorporateCatalog()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var console = SEntMan.SpawnEntity("AU14CorporateASRSConsole", map.GridCoords);
            var catalog = SComp<RequisitionsComputerComponent>(console);
            var crates = catalog.Categories
                .SelectMany(category => category.Entries)
                .Select(entry => entry.Crate.Id)
                .ToList();

            Assert.That(crates, Does.Contain("CMUWYSecurityBarrier"));
            Assert.That(crates, Does.Contain("CMUCrateM54C2"));
            Assert.That(crates, Does.Contain("CMUCrateNSG23"));
            Assert.That(crates, Does.Contain("CMUCrateF44AAMags"));
            Assert.That(crates, Does.Contain("CMUCrateMagazineM96S"));
            Assert.That(crates, Does.Contain("CMUCrateMagazineML66A"));

            var gear = catalog.Categories.Single(category => category.Name == "WEYSEC Gear");
            Assert.Multiple(() =>
            {
                Assert.That(gear.Entries.Any(entry => entry.Crate.Id == "RMCAttachmentLaserSight"), Is.True);
                Assert.That(gear.Entries.Any(entry => entry.Crate.Id == "RMCAttachmentU7UnderbarrelShotgun"), Is.True);
            });

            var crate = SEntMan.SpawnEntity("CMUCrateWYSecurityBarrier", map.GridCoords);
            Assert.That(SEntMan.HasComponent<CMUDeployOnOpenComponent>(crate), Is.True,
                "the barrier crate must deploy its barrier when opened");
        });
    }

    [Test]
    public async Task Nsg23CrateIncludesUnderbarrelFlamethrower()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;

            Assert.That(SProtoMan.TryIndex<EntityPrototype>("CMUCrateNSG23", out var crate), Is.True);
            Assert.That(crate!.TryComp<StorageFillComponent>(out var fill, factory), Is.True);
            Assert.That(fill!.Contents.Any(entry =>
                    entry.PrototypeId?.Id == "RMCWeaponRifleSSG45NoLock"),
                Is.True,
                "the purchased NSG-23 must use the variant that starts with attachments");

            Assert.That(SProtoMan.TryIndex<EntityPrototype>("RMCWeaponRifleSSG45NoLock", out var rifle), Is.True);
            Assert.That(rifle!.TryComp<AttachableHolderComponent>(out var holder, factory), Is.True);
            Assert.That(holder!.Slots["rmc-aslot-underbarrel"].StartingAttachable?.Id,
                Is.EqualTo("RMCAttachmentMiniFlamethrower"),
                "the purchased NSG-23 must come with an installed underbarrel flamethrower");
        });
    }

    [Test]
    public async Task GsbUsesIdLockedF44AA()
    {
        await Server.WaitAssertion(() =>
        {
            var gear = SProtoMan.Index<StartingGearPrototype>("AU14GearWYGuardLeader");
            Assert.That(gear.Equipment.TryGetValue("suitstorage", out var weapon), Is.True);
            Assert.That(weapon.Id, Is.EqualTo("AU14WeaponRifleF44AALock"));

            Assert.That(SProtoMan.TryIndex<EntityPrototype>(weapon, out var prototype), Is.True);
            Assert.That(prototype!.TryComp<GunIDLockComponent>(out _, SEntMan.ComponentFactory), Is.True,
                "the Head of Security's F44AA must support ID locking");
            Assert.That(prototype.TryComp<AttachableHolderComponent>(out var holder, SEntMan.ComponentFactory), Is.True);
            Assert.That(holder!.Slots["rmc-aslot-rail"].StartingAttachable?.Id,
                Is.EqualTo("RMCAttachmentTwoPointSling"),
                "the Head of Security's F44AA must keep its sling module");
        });
    }
}
