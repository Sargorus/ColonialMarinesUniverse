using Content.Server.CMU14.DroneOperator;
using Content.Server.CMU14.Robotics.UtilityDrones;
using Content.Shared._RMC14.Synth;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Hands.EntitySystems;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.Robotics;

[TestFixture]
public sealed class CMUDroneSystemBoardTest : GameTest
{
    [Test]
    public async Task ProgramAndCombatBoardsUseSeparateSlots()
    {
        var map = await Pair.CreateTestMap();
        EntityUid drone = default;
        EntityUid user = default;
        EntityUid screwdriver = default;
        EntityUid programBoard = default;
        EntityUid combatBoard = default;
        EntityUid directiveBoard = default;
        EntityUid multitool = default;

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var boards = SEntMan.System<CMUDroneSystemBoardSystem>();
            var drones = SEntMan.System<CMUUtilityDroneSystem>();

            user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            drone = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords.Offset(new(-2, 0)));
            programBoard = SEntMan.SpawnEntity("CMUUtilityDroneProgramSystemBoard", map.GridCoords);
            combatBoard = SEntMan.SpawnEntity("CMUUtilityDroneCombatSystemBoard", map.GridCoords);
            screwdriver = SEntMan.SpawnEntity("CMScrewdriver", map.GridCoords);
            directiveBoard = SEntMan.SpawnEntity("CMUUtilityDroneProgramBoard", map.GridCoords);

            var directiveComp = SEntMan.GetComponent<CMUUtilityDroneProgramBoardComponent>(directiveBoard);
            directiveComp.Script = "main { shoot 1 }";
            multitool = SEntMan.SpawnEntity("CMMultitool", map.GridCoords);

            Assert.That(hands.TryPickup(user, programBoard), Is.True);
            Assert.That(hands.TryPickupAnyHand(user, screwdriver), Is.True);
            Assert.That(
                boards.TryInstallSystemBoard(drone, programBoard, user, out var programError),
                Is.True,
                programError);

            var agent = SComp<CMUUtilityDroneAgentComponent>(drone);
            Assert.Multiple(() =>
            {
                Assert.That(boards.HasProgramSystem(drone), Is.True);
                Assert.That(agent.ProgrammableMode, Is.True);
                Assert.That(agent.BoardPortLocked, Is.False);
                Assert.That(boards.CanUseCombatPrograms(drone), Is.False);
            });

            Assert.That(hands.TryDrop(user, screwdriver), Is.True);
            Assert.That(hands.TryPickup(user, directiveBoard), Is.True);
            Assert.That(hands.TryPickupAnyHand(user, multitool), Is.True);
            Assert.That(
                drones.CanInstallProgramBoard(drone, directiveBoard, user, out var combatError),
                Is.False,
                "a peaceful program system cannot load combat commands");
            Assert.That(combatError, Is.Not.Empty);

            Assert.That(hands.TryDrop(user, directiveBoard), Is.True);
            Assert.That(hands.TryDrop(user, multitool), Is.True);
            Assert.That(hands.TryPickup(user, combatBoard), Is.True);
            Assert.That(hands.TryPickupAnyHand(user, screwdriver), Is.True);
            Assert.That(
                boards.TryInstallSystemBoard(drone, combatBoard, user, out var boardError),
                Is.True,
                boardError);

            Assert.Multiple(() =>
            {
                Assert.That(boards.HasCombatSystem(drone), Is.True);
                Assert.That(boards.CanUseCombatPrograms(drone), Is.True);
                Assert.That(SComp<SynthComponent>(drone).CanUseGuns, Is.True);
            });

            Assert.That(hands.TryDrop(user, screwdriver), Is.True);
            Assert.That(hands.TryPickup(user, directiveBoard), Is.True);
            Assert.That(hands.TryPickupAnyHand(user, multitool), Is.True);
            Assert.That(
                drones.CanInstallProgramBoard(drone, directiveBoard, user, out var allowedError),
                Is.True,
                allowedError);

            Assert.That(hands.TryDrop(user, directiveBoard), Is.True);
            Assert.That(hands.TryDrop(user, multitool), Is.True);
            Assert.That(hands.TryPickup(user, screwdriver), Is.True);
            Assert.That(
                boards.TryRemoveSystemBoard(drone, true, user, screwdriver, out var combatRemoveError),
                Is.True,
                combatRemoveError);

            Assert.Multiple(() =>
            {
                Assert.That(boards.HasCombatSystem(drone), Is.False);
                Assert.That(boards.CanUseCombatPrograms(drone), Is.False);
                Assert.That(SComp<SynthComponent>(drone).CanUseGuns, Is.False);
            });

            Assert.That(
                boards.TryRemoveSystemBoard(drone, false, user, screwdriver, out var programRemoveError),
                Is.True,
                programRemoveError);
            Assert.That(boards.HasProgramSystem(drone), Is.False);
            Assert.That(SEntMan.HasComponent<CMUUtilityDroneAgentComponent>(drone), Is.False);
        });
    }
}
