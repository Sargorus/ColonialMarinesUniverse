using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Content.Shared._RMC14.Dialog;
using Robust.Shared.Audio;

namespace Content.Server.CMU14.ColonyEconomy;

/// <summary>
/// Gives a safe its reinforced health and a player-managed PIN. Any user can
/// set the first PIN; changing an existing PIN requires the current code.
/// </summary>
public sealed partial class CMUSafeSystem : EntitySystem
{
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private DialogSystem _dialog = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUSafeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CMUSafeComponent, ActivateInWorldEvent>(OnActivate, before: [typeof(LockSystem)]);
        SubscribeLocalEvent<CMUSafeComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<CMUSafeComponent, CMUSafePinEvent>(OnPinEntered);
    }

    private void OnMapInit(Entity<CMUSafeComponent> ent, ref MapInitEvent args)
    {
        // List-valued YAML inheritance appends the parent's 100-damage closet
        // threshold, so replace the inherited thresholds instead of adding another.
        var destructible = EnsureComp<DestructibleComponent>(ent.Owner);
        _destructible.ReplaceThresholds(
            (ent.Owner, destructible),
            new List<DamageThreshold>
            {
                new()
                {
                    Trigger = new DamageTrigger { Damage = FixedPoint2.New(ent.Comp.HitPoints) },
                    Behaviors = new List<IThresholdBehavior>
                    {
                        new DoActsBehavior { Acts = ThresholdActs.Destruction },
                        new PlaySoundBehavior
                        {
                            Sound = new SoundCollectionSpecifier("MetalBreak")
                            {
                                Params = new AudioParams { Volume = -6 },
                            },
                        },
                    },
                },
            });

        // A safe without a PIN behaves as a normal locker.
        ent.Comp.Pin = string.Empty;
        ent.Comp.PinProtected = false;
        ClearPinChange(ent.Comp);
        if (TryComp<LockComponent>(ent.Owner, out var lockComp))
            _lock.Unlock(ent.Owner, null, lockComp);
    }

    private void OnActivate(Entity<CMUSafeComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled
            || !args.Complex
            || string.IsNullOrEmpty(ent.Comp.Pin))
            return;

        args.Handled = true;

        // An open safe closes on a normal left click. A closed safe asks for the PIN.
        if (TryComp<EntityStorageComponent>(ent.Owner, out var storage) && storage.Open)
        {
            _storage.TryCloseStorage(ent.Owner, args.User);
            return;
        }

        OpenPinDialog(ent, args.User, CMUSafePinAction.Unlock);
    }

    private void OnGetVerbs(Entity<CMUSafeComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;
        var hasPin = !string.IsNullOrEmpty(ent.Comp.Pin);

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(hasPin
                ? "cmu-safe-change-pin-verb"
                : "cmu-safe-set-pin-verb"),
            Priority = 3,
            Act = () => OpenPinDialog(
                ent,
                user,
                hasPin
                    ? CMUSafePinAction.VerifyCurrent
                    : CMUSafePinAction.Set),
        });
    }

    private void OpenPinDialog(
        Entity<CMUSafeComponent> ent,
        EntityUid user,
        CMUSafePinAction action)
    {
        if (action != CMUSafePinAction.Set)
            ClearPinChange(ent.Comp);

        var (title, prompt) = action switch
        {
            CMUSafePinAction.Set => (
                "cmu-safe-set-pin-title",
                "cmu-safe-set-pin-prompt"),
            CMUSafePinAction.VerifyCurrent => (
                "cmu-safe-current-pin-title",
                "cmu-safe-current-pin-prompt"),
            _ => (
                "cmu-safe-pin-title",
                "cmu-safe-pin-prompt"),
        };

        _dialog.OpenInput(
            ent.Owner,
            user,
            Loc.GetString(prompt),
            new CMUSafePinEvent(GetNetEntity(user), action),
            characterLimit: 4,
            minCharacterLimit: 4,
            title: Loc.GetString(title));
    }

    private void OnPinEntered(Entity<CMUSafeComponent> ent, ref CMUSafePinEvent args)
    {
        if (!TryGetEntity(args.Actor, out var actor)
            || actor is not { } user
            || TerminatingOrDeleted(user))
            return;

        // If the dialog still exists, its owner is the only actor allowed to answer.
        // DialogSystem closes the dialog before raising the input event, so the
        // actor carried by the event is the authoritative check.
        if (TryComp<DialogComponent>(ent.Owner, out var dialog) && dialog.Actor != user)
            return;

        if (args.Action == CMUSafePinAction.Set)
        {
            SetPin(ent, user, args.Message);
            return;
        }

        if (args.Action == CMUSafePinAction.VerifyCurrent)
        {
            VerifyCurrentPin(ent, user, args.Message);
            return;
        }

        if (string.IsNullOrEmpty(ent.Comp.Pin))
            return;

        if (args.Message != ent.Comp.Pin)
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-safe-pin-wrong"),
                ent,
                user,
                PopupType.SmallCaution);
            return;
        }

        if (TryComp<LockComponent>(ent.Owner, out var lockComp))
            _lock.Unlock(ent.Owner, user, lockComp);

        _popup.PopupEntity(Loc.GetString("cmu-safe-pin-accepted"), ent, user);
        _storage.TryOpenStorage(user, ent.Owner);
    }

    private void VerifyCurrentPin(Entity<CMUSafeComponent> ent, EntityUid user, string pin)
    {
        if (string.IsNullOrEmpty(ent.Comp.Pin))
            return;

        if (pin != ent.Comp.Pin)
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-safe-pin-wrong"),
                ent,
                user,
                PopupType.SmallCaution);
            return;
        }

        ent.Comp.PinChangeUser = user;
        ent.Comp.PinChangeVerified = true;
        OpenPinDialog(ent, user, CMUSafePinAction.Set);
    }

    private void SetPin(Entity<CMUSafeComponent> ent, EntityUid user, string pin)
    {
        var changing = !string.IsNullOrEmpty(ent.Comp.Pin);
        if (changing
            && (!ent.Comp.PinChangeVerified || ent.Comp.PinChangeUser != user))
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-safe-pin-current-required"),
                ent,
                user,
                PopupType.SmallCaution);
            return;
        }

        if (!IsValidPin(pin))
        {
            _popup.PopupEntity(
                Loc.GetString("cmu-safe-pin-invalid"),
                ent,
                user,
                PopupType.SmallCaution);
            return;
        }

        ent.Comp.Pin = pin;
        ent.Comp.PinProtected = true;
        ClearPinChange(ent.Comp);
        Dirty(ent.Owner, ent.Comp);
        _popup.PopupEntity(Loc.GetString("cmu-safe-pin-set"), ent, user);
    }

    private static void ClearPinChange(CMUSafeComponent component)
    {
        component.PinChangeUser = null;
        component.PinChangeVerified = false;
    }

    private static bool IsValidPin(string pin)
    {
        if (pin.Length != 4)
            return false;

        foreach (var character in pin)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }
}
