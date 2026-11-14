using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.UserInterface;
using Content.Shared._RMC14.Weapons.Ranged.IFF;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.CMU14.DroneOperator;

[TestFixture]
public sealed class CMUDroneConfigurationKeyTest : GameTest
{
    [Test]
    public async Task KeyCopiesChannelsAndCardAccessAndChangesFaction()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, user);
            SEntMan.EnsureComponent<NpcFactionMemberComponent>(user).Factions.Add("CLF");

            var drone = SEntMan.SpawnEntity("CMUDroneAndroidUnarmed", map.GridCoords.Offset(new(1, 0)));
            var govforDrone = SEntMan.SpawnEntity("CMUDroneAndroid", map.GridCoords.Offset(new(2, 0)));
            Assert.That(SComp<CMUDroneAndroidComponent>(drone).RelayOperatorSpeech, Is.False,
                "Weyland-Yutani fabricator drones must not relay their speech through the operator.");
            Assert.That(SComp<CMUDroneAndroidComponent>(govforDrone).RelayOperatorSpeech, Is.True,
                "GOVFOR and OPFOR issue drones must relay their speech through the operator.");
            var key = SEntMan.SpawnEntity("CMUDroneConfigurationKey", map.GridCoords.Offset(new(-1, 0)));
            var card = SEntMan.SpawnEntity("CMIDCardBase", map.GridCoords.Offset(new(0, -1)));

            var access = SEntMan.EnsureComponent<AccessComponent>(card);
            Assert.That(SEntMan.System<SharedAccessSystem>().TrySetTags(
                card,
                new[] { new ProtoId<AccessLevelPrototype>("AU14AccessCorporateWEYU") },
                access), Is.True);

            var transmitter = SEntMan.EnsureComponent<IntrinsicRadioTransmitterComponent>(user);
            transmitter.Channels = new HashSet<ProtoId<RadioChannelPrototype>> { "radioWEYU" };
            SEntMan.EnsureComponent<IntrinsicRadioReceiverComponent>(user);

            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, key), Is.True);

            var afterInteract = new AfterInteractEvent(user, key, drone, SEntMan.GetComponent<TransformComponent>(drone).Coordinates, true);
            SEntMan.EventBus.RaiseLocalEvent(key, afterInteract);
            Assert.That(afterInteract.Handled, Is.True);

            var session = SComp<CMUDroneConfigurationSessionComponent>(drone);
            Assert.That(session.HasIdCard, Is.False);
            Assert.That(session.AvailableAccess, Is.Empty);

            var state = (CMUDroneConfigurationBuiState) SComp<UserInterfaceComponent>(drone)
                .States[CMUDroneConfigurationUi.Key];
            Assert.That(state.Factions.Select(faction => faction.Id), Is.EquivalentTo(
                new[] { "AUWeYu", "GOVFOR", "OPFOR", "AUColonist", "CLF" }));
            Assert.That(state.Factions.Select(faction => faction.Id), Does.Not.Contain("THREAT"));

            // Picking up the card after the window opened must refresh the access list immediately.
            Assert.That(hands.TryPickupAnyHand(user, card), Is.True);
            var handContents = string.Join(", ",
                hands.EnumerateHands(user).Select(hand => $"{hand}={hands.GetHeldItem(user, hand)}"));
            Assert.That(session.HasIdCard, Is.True,
                $"session user={session.User} user={user} key={session.Key} card={card} hands={handContents} id={SEntMan.HasComponent<IdCardComponent>(card)} access={SEntMan.HasComponent<AccessComponent>(card)}");
            Assert.That(session.AvailableAccess, Does.Contain(new ProtoId<AccessLevelPrototype>("AU14AccessCorporateWEYU")));
            state = (CMUDroneConfigurationBuiState) SComp<UserInterfaceComponent>(drone)
                .States[CMUDroneConfigurationUi.Key];
            Assert.That(state.HasIdCard, Is.True);
            Assert.That(state.Access.Select(access => access.Id),
                Does.Contain("AU14AccessCorporateWEYU"));

            var message = new CMUDroneConfigurationApplyMsg(
                "AUWeYu",
                new HashSet<string> { "radioWEYU" },
                new HashSet<string> { "AU14AccessCorporateWEYU" })
            {
                Actor = user,
            };
            SEntMan.System<SharedUserInterfaceSystem>().RaiseUiMessage(drone, CMUDroneConfigurationUi.Key, message);

            Assert.That(SComp<NpcFactionMemberComponent>(drone).Factions,
                Does.Contain(new ProtoId<NpcFactionPrototype>("AUWeYu")));
            Assert.That(SComp<CMUDroneAndroidComponent>(drone).IffConfigured, Is.True);
            Assert.That(SComp<IntrinsicRadioTransmitterComponent>(drone).Channels,
                Does.Contain(new ProtoId<RadioChannelPrototype>("radioWEYU")));
            Assert.That(SComp<ActiveRadioComponent>(drone).Channels,
                Does.Contain(new ProtoId<RadioChannelPrototype>("radioWEYU")));
            Assert.That(SComp<AccessComponent>(drone).Tags,
                Does.Contain(new ProtoId<AccessLevelPrototype>("AU14AccessCorporateWEYU")));
            Assert.That(SComp<UserIFFComponent>(drone).Factions,
                Does.Contain(new EntProtoId<IFFFactionComponent>("FactionWEYU")));
        });
    }
}
