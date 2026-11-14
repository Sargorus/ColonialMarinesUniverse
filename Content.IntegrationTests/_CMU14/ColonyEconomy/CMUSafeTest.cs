using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Destructible;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Storage.Components;
using Content.Shared.Verbs;
using Content.Shared._RMC14.Dialog;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.ColonyEconomy;

[TestFixture]
public sealed class CMUSafeTest : GameTest
{
    [TestCase("RMCLockerSecureSafe")]
    [TestCase("RMCLockerSecureSafeColony")]
    [TestCase("RMCLockerSecureSafeLiaison")]
    [TestCase("RMCLockerIntelSafe")]
    public async Task SafeWithoutPinOpensAndAnyUserCanSetPin(string safePrototype)
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var safe = SEntMan.SpawnEntity(safePrototype, map.GridCoords);
            var safeComp = SComp<CMUSafeComponent>(safe);
            var storage = SComp<EntityStorageComponent>(safe);

            var user = SEntMan.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new(1, 0)));
            var hands = SComp<HandsComponent>(user);

            // The safe starts without a PIN and behaves as a normal locker.
            Assert.That(safeComp.Pin, Is.Empty);
            Assert.That(safeComp.PinProtected, Is.False);

            var openClick = new ActivateInWorldEvent(user, safe, true);
            SEntMan.EventBus.RaiseLocalEvent(safe, openClick);
            Assert.That(storage.Open, Is.True);

            var closeClick = new ActivateInWorldEvent(user, safe, true);
            SEntMan.EventBus.RaiseLocalEvent(safe, closeClick);
            Assert.That(storage.Open, Is.False);

            var destructible = SComp<DestructibleComponent>(safe);
            var thresholds = destructible.Thresholds
                .Select(entry => entry.Trigger)
                .OfType<DamageTrigger>()
                .Select(trigger => trigger.Damage)
                .ToList();
            Assert.That(thresholds, Does.Contain(FixedPoint2.New(safeComp.HitPoints)),
                "The safe's destruction threshold must come from its component.");
            Assert.That(thresholds, Does.Not.Contain(FixedPoint2.New(100)),
                "The inherited 100-damage closet threshold must be replaced.");

            // No ID card or special job is required to set the first PIN.
            var setVerb = GetVerb(safe, user, hands, "cmu-safe-set-pin-verb");
            setVerb.Act();
            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.Set,
                    "1234"));
            Assert.That(safeComp.Pin, Is.EqualTo("1234"));
            Assert.That(safeComp.PinProtected, Is.True);

            // A PIN-protected safe opens through the PIN dialog, while a normal
            // left click still closes it. The old lock/unlock verbs are redundant.
            var unlockClick = new ActivateInWorldEvent(user, safe, true);
            SEntMan.EventBus.RaiseLocalEvent(safe, unlockClick);
            Assert.That(unlockClick.Handled, Is.True);

            var alternativeVerbs = GetVerbs<AlternativeVerb>(safe, user, hands);
            SEntMan.EventBus.RaiseLocalEvent(safe, alternativeVerbs);
            Assert.That(alternativeVerbs.Verbs.Select(verb => verb.Text),
                Is.EquivalentTo(new[] { Loc.GetString("cmu-safe-change-pin-verb") }),
                "The safe must not offer redundant open/close or lock/unlock context-menu verbs.");

            var interactionVerbs = GetVerbs<InteractionVerb>(safe, user, hands);
            SEntMan.EventBus.RaiseLocalEvent(safe, interactionVerbs);
            Assert.That(interactionVerbs.Verbs.Any(verb =>
                    verb.Text == Loc.GetString("verb-common-open") ||
                    verb.Text == Loc.GetString("verb-common-close")),
                Is.False,
                "Left click already toggles the safe; the storage open/close verb is redundant.");

            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.Unlock,
                    "0000"));
            Assert.That(storage.Open, Is.False);

            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.Unlock,
                    "1234"));
            Assert.That(storage.Open, Is.True);

            // Changing the PIN requires the current PIN first.
            var changeVerb = GetVerb(safe, user, hands, "cmu-safe-change-pin-verb");
            changeVerb.Act();
            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.VerifyCurrent,
                    "0000"));
            Assert.That(safeComp.Pin, Is.EqualTo("1234"));

            changeVerb = GetVerb(safe, user, hands, "cmu-safe-change-pin-verb");
            changeVerb.Act();
            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.VerifyCurrent,
                    "1234"));
            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.Set,
                    "4321"));
            Assert.That(safeComp.Pin, Is.EqualTo("4321"));

            // A raw Set event without current-PIN verification must not change it.
            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.Set,
                    "9999"));
            Assert.That(safeComp.Pin, Is.EqualTo("4321"));

            var closeAgain = new ActivateInWorldEvent(user, safe, true);
            SEntMan.EventBus.RaiseLocalEvent(safe, closeAgain);
            Assert.That(storage.Open, Is.False);

            var reopenClick = new ActivateInWorldEvent(user, safe, true);
            SEntMan.EventBus.RaiseLocalEvent(safe, reopenClick);
            Assert.That(reopenClick.Handled, Is.True);

            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.Unlock,
                    "1234"));
            Assert.That(storage.Open, Is.False);

            SEntMan.EventBus.RaiseLocalEvent(safe,
                new CMUSafePinEvent(
                    SEntMan.GetNetEntity(user),
                    CMUSafePinAction.Unlock,
                    "4321"));
            Assert.That(storage.Open, Is.True);
        });
    }

    private GetVerbsEvent<TVerb> GetVerbs<TVerb>(
        EntityUid safe,
        EntityUid user,
        HandsComponent hands) where TVerb : Verb
    {
        return new GetVerbsEvent<TVerb>(
            user,
            safe,
            null,
            hands,
            true,
            true,
            true,
            new List<VerbCategory>());
    }

    private AlternativeVerb GetVerb(
        EntityUid safe,
        EntityUid user,
        HandsComponent hands,
        string locId)
    {
        var verbs = GetVerbs<AlternativeVerb>(safe, user, hands);
        SEntMan.EventBus.RaiseLocalEvent(safe, verbs);
        return verbs.Verbs.Single(verb => verb.Text == Loc.GetString(locId));
    }
}
