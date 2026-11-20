using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Content.Shared.Tools;
using Content.Shared.Timing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared._RMC14.Repairable;
using Content.Shared._RMC14.Synth;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Combat and repair script support. Every shot goes through the normal gun
/// system so synth gun restrictions, skill penalties, IFF, wield delays and
/// Z-level shooting all keep working. The fight/repair controllers are built
/// from the same primitives exposed to scripts.
/// </summary>
public sealed partial class CMUUtilityDroneSystem
{
    [Dependency] private RMCRepairableSystem _repairable = default!;
    [Dependency] private SharedStackSystem _stack = default!;

    private const string ScriptWieldDelayId = "RMCWieldDelay";
    private const float FightTimeoutSeconds = 120f;
    private const float RepairTimeoutSeconds = 120f;
    private const int MaxCombatShots = 240;
    private const int MaxRepairCycles = 12;

    #region Natives

    private CMUUtilityScriptValue NativeHealth(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!target.IsValid() || TerminatingOrDeleted(target))
            return CMUUtilityScriptValue.FromNumber(-1);

        var max = GetMaxHealth(target);
        if (max <= 0)
            return CMUUtilityScriptValue.FromNumber(-1);

        return CMUUtilityScriptValue.FromNumber(
            Math.Max(0, max - GetTotalDamage(target)));
    }

    private CMUUtilityScriptValue NativeMaxHealth(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!target.IsValid() || TerminatingOrDeleted(target))
            return CMUUtilityScriptValue.FromNumber(-1);

        return CMUUtilityScriptValue.FromNumber(GetMaxHealth(target));
    }

    private CMUUtilityScriptValue NativeHealthPercent(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!target.IsValid() || TerminatingOrDeleted(target))
            return CMUUtilityScriptValue.FromNumber(-1);

        var max = GetMaxHealth(target);
        if (max <= 0)
            return CMUUtilityScriptValue.FromNumber(-1);

        var percent = (max - GetTotalDamage(target)) / max * 100;
        return CMUUtilityScriptValue.FromNumber(Math.Clamp(percent, 0, 100));
    }

    private CMUUtilityScriptValue NativeAlive(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!target.IsValid() || TerminatingOrDeleted(target))
            return CMUUtilityScriptValue.FromBool(false);

        return CMUUtilityScriptValue.FromBool(
            !TryComp<MobStateComponent>(target, out var mob) ||
            _mobState.IsAlive(target, mob));
    }

    private CMUUtilityScriptValue NativeDead(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        return CMUUtilityScriptValue.FromBool(
            target.IsValid() &&
            !TerminatingOrDeleted(target) &&
            TryComp<MobStateComponent>(target, out var mob) &&
            _mobState.IsDead(target, mob));
    }

    private CMUUtilityScriptValue NativeCritical(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        return CMUUtilityScriptValue.FromBool(
            target.IsValid() &&
            !TerminatingOrDeleted(target) &&
            TryComp<MobStateComponent>(target, out var mob) &&
            _mobState.IsCritical(target, mob));
    }

    private CMUUtilityScriptValue NativeNearestHostile(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        return TryFindNearestHostile(uid, radius, out var target)
            ? CMUUtilityScriptValue.FromEntity(target)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeHostiles(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        var entries = GetNearbyHostiles(uid, radius)
            .Select(hostile => new CMUUtilityNearbyInfo(
                hostile,
                MetaData(hostile).EntityName,
                Vector2.Distance(
                    _transform.GetWorldPosition(uid),
                    _transform.GetWorldPosition(hostile))))
            .OrderBy(hostile => hostile.Distance)
            .Take(CMUUtilityDroneLimits.MaxInfoEntries)
            .Select(hostile => Loc.GetString(
                "cmu-utility-drone-script-info-distance",
                ("name", hostile.Name),
                ("distance", hostile.Distance.ToString("0.#", CultureInfo.InvariantCulture))))
            .ToList();

        return CMUUtilityScriptValue.FromString(FormatEntries(entries));
    }

    private CMUUtilityScriptValue NativeNearestAlly(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        return TryFindNearbyAlly(uid, radius, requireDamage: false, requireRepair: false, out var ally)
            ? CMUUtilityScriptValue.FromEntity(ally)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeAmmo(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        return TryResolveGunValue(uid, args, out var gun)
            ? CMUUtilityScriptValue.FromNumber(_gun.GetAmmoCount(gun))
            : CMUUtilityScriptValue.FromNumber(-1);
    }

    private CMUUtilityScriptValue NativeMaxAmmo(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        return TryResolveGunValue(uid, args, out var gun)
            ? CMUUtilityScriptValue.FromNumber(_gun.GetAmmoCapacity(gun))
            : CMUUtilityScriptValue.FromNumber(-1);
    }

    private CMUUtilityScriptValue NativeInCombat(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (!TryGetAgentTarget(uid, args, out var agent))
            return CMUUtilityScriptValue.FromBool(false);

        if (_timing.CurTime < agent.CombatUntil)
            return CMUUtilityScriptValue.FromBool(true);

        var execution = GetScriptExecution(uid);
        return CMUUtilityScriptValue.FromBool(
            execution?.Main.Action?.Kind == CMUUtilityScriptActionKind.Fight ||
            execution?.ActiveHandler?.Action?.Kind == CMUUtilityScriptActionKind.Fight);
    }

    private int CountHostiles(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        var count = 0;
        foreach (var hostile in GetNearbyHostiles(uid, radius))
        {
            if (hostile.IsValid())
                count++;
        }

        return count;
    }

    #endregion

    #region Aim, wield and shooting

    private bool TryWieldScriptItem(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptValue value,
        out string failure)
    {
        failure = "cmu-utility-drone-script-fail-item";
        EntityUid item;
        if (value.Type == CMUUtilityScriptValueType.Entity &&
            value.Entity.IsValid() &&
            !TerminatingOrDeleted(value.Entity))
        {
            item = value.Entity;
        }
        else if (string.IsNullOrWhiteSpace(value.AsText))
        {
            if (!_hands.TryGetActiveItem(uid, out var active))
            {
                failure = "cmu-utility-drone-script-fail-hand";
                return false;
            }

            item = active.Value;
        }
        else if (!TryResolveHeldItem(uid, value, out item) &&
                 !TryResolveItemValue(uid, value, out item))
        {
            return false;
        }

        if (!_hands.EnumerateHeld(uid).Contains(item) &&
            !_hands.TryPickupAnyHand(uid, item))
        {
            failure = "cmu-utility-drone-script-fail-hand";
            return false;
        }

        if (TryComp<WieldableComponent>(item, out var wieldable) &&
            !wieldable.Wielded &&
            !_wieldable.TryWield((item, wieldable), uid))
        {
            failure = "cmu-utility-drone-script-fail-wield";
            return false;
        }

        foreach (var handId in _hands.EnumerateHands(uid))
        {
            if (_hands.TryGetHeldItem(uid, handId, out var held) && held == item)
            {
                _hands.TrySetActiveHand(uid, handId);
                break;
            }
        }

        agent.CombatWeapon = item;
        return true;
    }

    private void UnwieldScriptWeapon(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        var item = agent.CombatWeapon;
        if (item is not { } wielded ||
            TerminatingOrDeleted(wielded) ||
            !_hands.EnumerateHeld(uid).Contains(wielded))
        {
            if (!TryGetHeldGun(uid, out wielded))
                return;
        }

        if (TryComp<WieldableComponent>(wielded, out var wieldable) &&
            wieldable.Wielded)
        {
            _wieldable.TryUnwield((wielded, wieldable), uid);
        }

        agent.CombatWeapon = null;
        agent.CombatTarget = null;
        agent.CombatCoordinates = null;
    }

    private bool SetScriptAimEntity(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptValue value)
    {
        if (!TryResolveCombatTarget(uid, value, out var target))
            return false;

        agent.CombatTarget = target;
        agent.CombatCoordinates = Transform(target).Coordinates;
        FaceCoordinates(uid, Transform(target).Coordinates);
        return true;
    }

    private void SetScriptAimCoordinates(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        float x,
        float y)
    {
        var coordinates = Transform(uid).Coordinates.WithPosition(new Vector2(x, y));
        agent.CombatTarget = null;
        agent.CombatCoordinates = coordinates;
        FaceCoordinates(uid, coordinates);
    }

    private void ClearScriptAim(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        agent.CombatTarget = null;
        agent.CombatCoordinates = null;
    }

    private void StartShootScriptAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid? target,
        EntityCoordinates? coordinates,
        int count)
    {
        if (!TryGetHeldGun(uid, out var gun))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-gun");
            return;
        }

        if (target is null &&
            coordinates is null &&
            agent.CombatTarget is { } aimedTarget &&
            aimedTarget.IsValid() &&
            !TerminatingOrDeleted(aimedTarget))
        {
            target = aimedTarget;
        }

        if (target is null &&
            coordinates is null)
        {
            coordinates = agent.CombatCoordinates;
        }

        if (target is null &&
            coordinates is null)
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-aim");
            return;
        }

        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.Shoot,
            target);
        action.Item = gun;
        action.Coordinates = coordinates;
        action.Counter = Math.Clamp(count, 1, 100);
        action.AutoTarget = target is null;
        if (target is { } aimedShotTarget)
        {
            agent.CombatTarget = aimedShotTarget;
            agent.CombatCoordinates = Transform(aimedShotTarget).Coordinates;
        }
        else if (coordinates is { } aimedShotCoordinates)
        {
            agent.CombatTarget = null;
            agent.CombatCoordinates = aimedShotCoordinates;
        }

        agent.CombatWeapon = gun;
        agent.CombatUntil = _timing.CurTime + TimeSpan.FromSeconds(15);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-shooting");
    }

    private bool TickShootScriptAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame)
    {
        if (frame.Action is not { } action)
        {
            frame.Action = null;
            return false;
        }

        if (action.Counter <= 0)
        {
            frame.Action = null;
            return false;
        }

        if (_timing.CurTime - action.Started > TimeSpan.FromSeconds(30))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-shoot");
            return true;
        }

        if (action.Item is not { } gun ||
            TerminatingOrDeleted(gun) ||
            !_hands.EnumerateHeld(uid).Contains(gun) ||
            !TryComp<GunComponent>(gun, out var gunComp))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-item");
            return true;
        }

        if (agent.WeaponsHold)
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-hold-fire");
            return true;
        }

        var now = _timing.CurTime;
        if (TryComp<UseDelayComponent>(gun, out var useDelay) &&
            _useDelay.IsDelayed((gun, useDelay), ScriptWieldDelayId))
        {
            return true;
        }

        if (now < gunComp.NextFire)
            return true;

        EntityCoordinates coordinates;
        EntityUid? target = action.Target;
        if (target is { } currentTarget)
        {
            if (TerminatingOrDeleted(currentTarget) ||
                !TryComp<MobStateComponent>(currentTarget, out var mob) ||
                _mobState.IsDead(currentTarget, mob))
            {
                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
                return true;
            }

            coordinates = Transform(currentTarget).Coordinates;
            FaceCoordinates(uid, coordinates);
        }
        else if (action.Coordinates is { } aimedCoordinates &&
                 aimedCoordinates.IsValid(EntityManager))
        {
            coordinates = aimedCoordinates;
            FaceCoordinates(uid, coordinates);
        }
        else
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-aim");
            return true;
        }

        if (!TryShootGun(uid, gun, gunComp, coordinates, target, out var failure))
        {
            frame.Failure = Loc.GetString(failure);
            return true;
        }

        action.Counter--;
        agent.CombatUntil = now + TimeSpan.FromSeconds(15);
        if (action.Counter <= 0)
        {
            frame.Action = null;
            return false;
        }

        return true;
    }

    private bool TryShootGun(
        EntityUid uid,
        EntityUid gun,
        GunComponent gunComp,
        EntityCoordinates coordinates,
        EntityUid? target,
        out string failure)
    {
        failure = "cmu-utility-drone-script-fail-shoot";
        if (!coordinates.IsValid(EntityManager))
        {
            failure = "cmu-utility-drone-script-fail-target";
            return false;
        }

        if (_gun.GetAmmoCount(gun) <= 0)
        {
            failure = "cmu-utility-drone-script-fail-ammo";
            return false;
        }

        FaceCoordinates(uid, coordinates);
        return _gun.AttemptShoot(uid, (gun, gunComp), coordinates, target);
    }

    private void FaceCoordinates(EntityUid uid, EntityCoordinates coordinates)
    {
        var target = _transform.ToMapCoordinates(coordinates);
        if (target.MapId != Transform(uid).MapID)
            return;

        var delta = target.Position - _transform.GetWorldPosition(uid);
        if (delta.LengthSquared() < 0.0001f)
            return;

        _transform.SetWorldRotation(uid, new Angle(MathF.Atan2(delta.Y, delta.X)));
    }

    private bool TryGetHeldGun(EntityUid uid, out EntityUid gun)
    {
        gun = default;
        if (TryComp<CMUUtilityDroneAgentComponent>(uid, out var agent) &&
            agent.CombatWeapon is { } stored &&
            !TerminatingOrDeleted(stored) &&
            _hands.EnumerateHeld(uid).Contains(stored) &&
            HasComp<GunComponent>(stored))
        {
            gun = stored;
            return true;
        }

        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!HasComp<GunComponent>(held))
                continue;

            gun = held;
            return true;
        }

        return false;
    }

    private bool TryFetchGunFromBackpack(EntityUid uid, out EntityUid gun)
    {
        gun = default;
        if (!TryGetBackpack(uid, out var backpack))
            return false;

        foreach (var candidate in EnumerateBackpackItems(backpack).ToList())
        {
            if (TerminatingOrDeleted(candidate) || !HasComp<GunComponent>(candidate))
                continue;

            if (!RemoveFromBackpack(backpack, candidate))
                continue;

            if (_hands.TryPickupAnyHand(uid, candidate))
            {
                gun = candidate;
                return true;
            }

            InsertBackpackItem(backpack, candidate, uid);
        }

        return false;
    }

    private bool TryResolveGunValue(
        EntityUid uid,
        CMUUtilityScriptValue[] args,
        out EntityUid gun)
    {
        gun = default;
        if (args.Length == 0)
            return TryGetHeldGun(uid, out gun);

        if (args[0].Type == CMUUtilityScriptValueType.Entity &&
            args[0].Entity.IsValid() &&
            !TerminatingOrDeleted(args[0].Entity) &&
            HasComp<GunComponent>(args[0].Entity))
        {
            gun = args[0].Entity;
            return true;
        }

        if (args[0].Type != CMUUtilityScriptValueType.String ||
            string.IsNullOrWhiteSpace(args[0].Text))
        {
            return false;
        }

        if (TryResolveHeldItem(uid, args[0], out gun) && HasComp<GunComponent>(gun))
            return true;

        return TryFindWorldItem(uid, args[0].Text, 3f, out gun) &&
               HasComp<GunComponent>(gun);
    }

    private bool TryResolveCombatTarget(
        EntityUid uid,
        CMUUtilityScriptValue value,
        out EntityUid target)
    {
        target = default;
        if (value.Type == CMUUtilityScriptValueType.Entity &&
            value.Entity.IsValid() &&
            !TerminatingOrDeleted(value.Entity))
        {
            target = value.Entity;
            return true;
        }

        if (value.Type != CMUUtilityScriptValueType.String ||
            string.IsNullOrWhiteSpace(value.Text))
        {
            return false;
        }

        return TryFindPerson(uid, value.Text, CMUUtilityDroneLimits.MaxRadius, out target) ||
               TryFindWorldItem(uid, value.Text, CMUUtilityDroneLimits.MaxRadius, out target);
    }

    #endregion

    #region Reloading

    private bool ReloadScriptWeapon(EntityUid uid, out string failure)
    {
        failure = "cmu-utility-drone-script-fail-gun";
        if (!TryGetHeldGun(uid, out var gun))
            return false;

        failure = "cmu-utility-drone-script-fail-reload";
        return TryReloadGun(uid, gun);
    }

    private bool TryReloadGun(EntityUid uid, EntityUid gun)
    {
        if (!TryComp<ItemSlotsComponent>(gun, out var itemSlots) ||
            !_slots.TryGetSlot((gun, itemSlots), SharedGunSystem.MagazineSlot, out var slot))
        {
            return false;
        }

        if (_gun.GetAmmoCount(gun) > 0)
            return true;

        if (slot.HasItem && !_slots.TryEjectToHands(gun, slot, uid))
            return false;

        var candidates = new List<EntityUid>(_hands.EnumerateHeld(uid));
        if (TryGetBackpack(uid, out var backpack))
            candidates.AddRange(EnumerateBackpackItems(backpack));

        foreach (var candidate in candidates)
        {
            if (candidate == gun ||
                TerminatingOrDeleted(candidate) ||
                !HasComp<BallisticAmmoProviderComponent>(candidate) ||
                _gun.GetAmmoCount(candidate) <= 0)
            {
                continue;
            }

            var fromBackpack = false;
            if (!_hands.EnumerateHeld(uid).Contains(candidate))
            {
                if (backpack == default ||
                    !EnumerateBackpackItems(backpack).Contains(candidate) ||
                    !RemoveFromBackpack(backpack, candidate))
                {
                    continue;
                }

                fromBackpack = true;
            }

            if (!_slots.CanInsert(gun, slot, candidate, uid))
            {
                if (fromBackpack)
                    InsertBackpackItem(backpack, candidate, uid);

                continue;
            }

            if (_slots.TryInsert(gun, slot, candidate, uid))
                return true;

            if (fromBackpack)
                InsertBackpackItem(backpack, candidate, uid);
        }

        return false;
    }

    #endregion

    #region Fight controller

    private void StartFightAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        EntityUid? target,
        float radius)
    {
        if (!TryGetHeldGun(uid, out var gun) &&
            !TryFetchGunFromBackpack(uid, out gun))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-gun");
            return;
        }

        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.Fight,
            target);
        action.Item = gun;
        action.Text = target is null ? "auto" : "target";
        action.Radius = Math.Clamp(radius, 2f, CMUUtilityDroneLimits.MaxRadius);
        action.Seconds = FightTimeoutSeconds;
        action.AutoTarget = target is null;
        action.WaitUntil = TimeSpan.Zero;
        agent.CombatWeapon = gun;
        agent.CombatAbort = false;
        agent.CombatUntil = _timing.CurTime + TimeSpan.FromSeconds(15);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-fighting");
    }

    private bool TickFightScriptAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame)
    {
        if (frame.Action is not { } action)
        {
            frame.Action = null;
            return false;
        }

        var now = _timing.CurTime;
        if (agent.CombatAbort)
        {
            agent.CombatAbort = false;
            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-disengaged");
            return false;
        }

        if (now - action.Started > TimeSpan.FromSeconds(action.Seconds))
        {
            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-fight-timeout");
            return false;
        }

        if (action.Item is not { } weapon ||
            TerminatingOrDeleted(weapon) ||
            !_hands.EnumerateHeld(uid).Contains(weapon) ||
            !TryComp<GunComponent>(weapon, out var gunComp))
        {
            if (!TryGetHeldGun(uid, out weapon) &&
                !TryFetchGunFromBackpack(uid, out weapon))
            {
                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-gun");
                return true;
            }

            if (!TryComp<GunComponent>(weapon, out gunComp))
            {
                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-gun");
                return true;
            }

            action.Item = weapon;
            agent.CombatWeapon = weapon;
        }

        if (TryComp<WieldableComponent>(weapon, out var wieldable) &&
            !wieldable.Wielded)
        {
            if (!_wieldable.TryWield((weapon, wieldable), uid))
            {
                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-wield");
                return true;
            }

            return true;
        }

        if (TryComp<UseDelayComponent>(weapon, out var useDelay) &&
            _useDelay.IsDelayed((weapon, useDelay), ScriptWieldDelayId))
        {
            return true;
        }

        EntityUid target;
        if (action.AutoTarget)
        {
            if (agent.CombatTarget is { } current &&
                IsValidHostileTarget(uid, current))
            {
                target = current;
            }
            else if (!TryFindNearestHostile(uid, action.Radius, out target))
            {
                ClearScriptAim(uid, agent);
                frame.Action = null;
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-no-target");
                return false;
            }
        }
        else if (action.Target is { } explicitTarget &&
                 explicitTarget.IsValid() &&
                 !TerminatingOrDeleted(explicitTarget) &&
                 IsCombatTargetAlive(explicitTarget))
        {
            target = explicitTarget;
        }
        else
        {
            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-target-lost");
            return false;
        }

        if (IsFriendlyTarget(uid, target))
        {
            if (action.AutoTarget)
            {
                agent.CombatTarget = null;
                return true;
            }

            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-friendly-target");
            return false;
        }

        if (!IsCombatTargetAlive(target))
        {
            if (action.AutoTarget)
            {
                agent.CombatTarget = null;
                return true;
            }

            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-target-lost");
            return false;
        }

        agent.CombatTarget = target;
        var origin = _transform.GetWorldPosition(uid);
        var targetPosition = _transform.GetWorldPosition(target);
        var distance = Vector2.Distance(origin, targetPosition);
        var desiredRange = Math.Clamp(action.Radius * 0.6f, 2f, 8f);

        if (distance > Math.Max(action.Radius * 1.5f, action.Radius + 4f))
        {
            if (action.AutoTarget)
            {
                agent.CombatTarget = null;
                return true;
            }

            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-target-lost");
            return false;
        }

        if (distance > action.Radius)
        {
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-closing");
            var move = MoveToEntity(uid, agent, target, desiredRange);
            if (move == MoveResult.Failed)
            {
                if (action.AutoTarget)
                {
                    agent.CombatTarget = null;
                    return true;
                }

                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-path");
                return true;
            }

            return true;
        }

        if (!HasLineOfFire(uid, target, distance))
        {
            if (distance > 2.5f)
            {
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-closing");
                var move = MoveToEntity(uid, agent, target, 1.8f);
                if (move == MoveResult.Failed)
                {
                    if (action.AutoTarget)
                    {
                        agent.CombatTarget = null;
                        return true;
                    }

                    frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-path");
                    return true;
                }

                return true;
            }

            if (now - action.Started < TimeSpan.FromSeconds(3))
                return true;

            if (action.AutoTarget)
            {
                agent.CombatTarget = null;
                return true;
            }

            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-line-of-fire");
            return true;
        }

        if (_gun.GetAmmoCount(weapon) <= 0)
        {
            if (TryReloadGun(uid, weapon))
            {
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-reloading");
                return true;
            }

            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-out-of-ammo");
            return false;
        }

        if (agent.WeaponsHold)
        {
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-holding-fire");
            return true;
        }

        if (now < gunComp.NextFire)
            return true;

        var coordinates = Transform(target).Coordinates;
        if (!TryShootGun(uid, weapon, gunComp, coordinates, target, out var failure))
        {
            if (failure == "cmu-utility-drone-script-fail-ammo" &&
                TryReloadGun(uid, weapon))
            {
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-reloading");
                return true;
            }

            frame.Failure = Loc.GetString(failure);
            return true;
        }

        action.Counter++;
        agent.CombatUntil = now + TimeSpan.FromSeconds(15);
        if (action.Counter >= MaxCombatShots)
        {
            ClearScriptAim(uid, agent);
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-fight-complete");
            return false;
        }

        return true;
    }

    private bool TryFindNearestHostile(EntityUid uid, float radius, out EntityUid target)
    {
        target = default;
        if (!HasComp<NpcFactionMemberComponent>(uid))
            return false;

        var bestDistance = float.MaxValue;
        var origin = _transform.GetWorldPosition(uid);
        var scanned = 0;
        foreach (var candidate in _faction.GetNearbyHostiles(uid, radius))
        {
            if (scanned++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;

            if (!IsValidHostileTarget(uid, candidate))
                continue;

            var distance = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(candidate));
            if (distance >= bestDistance)
                continue;

            target = candidate;
            bestDistance = distance;
        }

        return target.IsValid();
    }

    private IEnumerable<EntityUid> GetNearbyHostiles(EntityUid uid, float radius)
    {
        if (!HasComp<NpcFactionMemberComponent>(uid))
            yield break;

        var scanned = 0;
        foreach (var candidate in _faction.GetNearbyHostiles(uid, radius))
        {
            if (scanned++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                yield break;

            if (IsValidHostileTarget(uid, candidate))
                yield return candidate;
        }
    }

    private bool IsValidHostileTarget(EntityUid uid, EntityUid candidate)
    {
        if (candidate == uid ||
            TerminatingOrDeleted(candidate) ||
            HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
            HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
            !TryComp<MobStateComponent>(candidate, out var mob) ||
            !_mobState.IsAlive(candidate, mob) ||
            !HasComp<DamageableComponent>(candidate) ||
            !SameZNetwork(uid, candidate))
        {
            return false;
        }

        return IsHostileTo(uid, candidate);
    }

    private bool IsHostileTo(EntityUid uid, EntityUid candidate)
    {
        if (!TryComp<NpcFactionMemberComponent>(uid, out var own) ||
            !TryComp<NpcFactionMemberComponent>(candidate, out var other) ||
            _faction.IsEntityFriendly((uid, own), (candidate, other)))
        {
            return false;
        }

        return own.HostileFactions.Overlaps(other.Factions);
    }

    private bool IsFriendlyTarget(EntityUid uid, EntityUid candidate)
    {
        return TryComp<NpcFactionMemberComponent>(uid, out var own) &&
               TryComp<NpcFactionMemberComponent>(candidate, out var other) &&
               _faction.IsEntityFriendly((uid, own), (candidate, other));
    }

    private bool IsCombatTargetAlive(EntityUid target)
    {
        return target.IsValid() &&
               !TerminatingOrDeleted(target) &&
               TryComp<MobStateComponent>(target, out var mob) &&
               _mobState.IsAlive(target, mob);
    }

    private bool HasLineOfFire(EntityUid uid, EntityUid target, float distance)
    {
        if (distance <= 1.5f)
            return true;

        return _interaction.InRangeUnobstructed(
            uid,
            target,
            distance + 0.5f,
            CollisionGroup.Impassable | CollisionGroup.InteractImpassable,
            entity => entity == uid || entity == target);
    }

    private void DisengageScriptCombat(EntityUid uid, CMUUtilityDroneAgentComponent agent)
    {
        agent.CombatAbort = true;
        agent.CombatUntil = TimeSpan.Zero;
        UnwieldScriptWeapon(uid, agent);
        ClearScriptAim(uid, agent);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-disengaged");
    }

    #endregion

    #region Repair controller

    private void StartRepairAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame,
        string mode,
        EntityUid? target,
        float radius)
    {
        var action = PrepareScriptAction(
            uid,
            agent,
            frame,
            CMUUtilityScriptActionKind.Repair,
            target);
        action.Text = mode;
        action.Radius = Math.Clamp(radius, CMUUtilityDroneLimits.MinRadius, CMUUtilityDroneLimits.MaxRadius);
        action.Seconds = RepairTimeoutSeconds;
        action.Phase = 0;
        action.Counter = 0;
        action.Attempts = 0;
        action.WaitUntil = TimeSpan.Zero;
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-repair-search");
    }

    private bool TickRepairScriptAction(
        EntityUid uid,
        CMUUtilityDroneAgentComponent agent,
        CMUUtilityScriptFrame frame)
    {
        if (frame.Action is not { } action)
        {
            frame.Action = null;
            return false;
        }

        var now = _timing.CurTime;
        if (now - action.Started > TimeSpan.FromSeconds(action.Seconds))
        {
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-repair-timeout");
            return false;
        }

        if (action.Phase == 0)
        {
            if (HasNearbyHostiles(uid, action.Radius))
            {
                frame.Action = null;
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-repair-unsafe");
                return false;
            }

            if (!TrySelectRepairTarget(uid, action, out var selected))
            {
                frame.Action = null;
                agent.Activity = Loc.GetString("cmu-utility-drone-activity-repair-none");
                return false;
            }

            action.Target = selected;
            action.Phase = 1;
            action.Attempts = 0;
            action.WaitUntil = TimeSpan.Zero;
        }

        if (action.Target is not { } target ||
            TerminatingOrDeleted(target))
        {
            frame.Action = null;
            return false;
        }

        if (HasNearbyHostiles(uid, action.Radius))
        {
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-repair-unsafe");
            return false;
        }

        var damage = GetTotalDamage(target);
        if (damage <= 0)
        {
            frame.Action = null;
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-repair-complete");
            return false;
        }

        if (action.Attempts >= MaxRepairCycles)
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-repair");
            return true;
        }

        if (target != uid)
        {
            agent.Activity = Loc.GetString("cmu-utility-drone-activity-repairing");
            var move = MoveToEntity(uid, agent, target, 1.5f);
            if (move == MoveResult.Failed)
            {
                frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-path");
                return true;
            }

            if (move != MoveResult.Arrived)
                return true;
        }

        if (!TryComp<SynthComponent>(target, out var synth))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-target");
            return true;
        }

        if (action.WaitUntil != TimeSpan.Zero)
        {
            if (now < action.WaitUntil)
                return true;

            ApplyScriptRepair(uid, target, synth, action);
            action.WaitUntil = TimeSpan.Zero;
            action.Attempts++;
            return true;
        }

        if (!HasDamageGroup(target, synth.WelderDamageGroup) &&
            !HasDamageGroup(target, synth.CableCoilDamageGroup))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-script-fail-repair");
            return true;
        }

        if (!TryGetScriptRepairTool(uid, target, synth, out var tool, out var cable))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-activity-repair-tool");
            return true;
        }

        if (!cable &&
            TryComp<ItemToggleComponent>(tool, out var toggle) &&
            !toggle.Activated &&
            !_itemToggle.TryActivate(tool, uid, predicted: false, showPopup: false))
        {
            frame.Failure = Loc.GetString("cmu-utility-drone-activity-repair-tool");
            return true;
        }

        _steering.Unregister(uid);
        agent.Activity = Loc.GetString("cmu-utility-drone-activity-repairing");
        action.Item = tool;

        var delay = target == uid ? synth.SelfRepairTime : synth.RepairTime;
        action.WaitUntil = now + TimeSpan.FromSeconds(
            Math.Clamp(delay.TotalSeconds, 1, 30));
        return true;
    }

    private void ApplyScriptRepair(
        EntityUid uid,
        EntityUid target,
        SynthComponent synth,
        CMUUtilityScriptActionState action)
    {
        if (action.Item is not { } tool || TerminatingOrDeleted(tool))
            return;

        if (HasComp<RMCCableCoilComponent>(tool))
        {
            if (!_stack.TryUse(tool, 1))
                return;

            if (synth.CableCoilDamageToRepair is { } cableHeal)
                _damageable.TryChangeDamage(target, cableHeal, true, false, origin: uid);

            return;
        }

        if (!_repairable.UseFuel(tool, uid, 5))
            return;

        if (synth.WelderDamageToRepair is { } welderHeal)
            _damageable.TryChangeDamage(target, welderHeal, true, false, origin: uid);
    }

    private bool HasDamageGroup(EntityUid target, ProtoId<DamageGroupPrototype> group)
    {
        if (!TryComp<DamageableComponent>(target, out var damageable))
            return false;

        var perGroup = _damageable.GetDamagePerGroup((target, damageable));
        return perGroup.TryGetValue(group, out var amount) && amount > 0;
    }

    private bool TrySelectRepairTarget(
        EntityUid uid,
        CMUUtilityScriptActionState action,
        out EntityUid target)
    {
        target = default;
        switch (action.Text)
        {
            case "self":
                target = uid;
                return CanRepairTarget(uid);

            case "target":
                if (action.Target is not { } explicitTarget ||
                    TerminatingOrDeleted(explicitTarget) ||
                    (TryComp<MobStateComponent>(explicitTarget, out var explicitMob) &&
                     _mobState.IsDead(explicitTarget, explicitMob)) ||
                    IsHostileTo(uid, explicitTarget))
                {
                    return false;
                }

                target = explicitTarget;
                return CanRepairTarget(target);

            case "ally":
                return TryFindNearbyAlly(
                    uid,
                    action.Radius,
                    requireDamage: true,
                    requireRepair: true,
                    out target);

            default:
                if (CanRepairTarget(uid))
                {
                    target = uid;
                    return true;
                }

                return TryFindNearbyAlly(
                    uid,
                    action.Radius,
                    requireDamage: true,
                    requireRepair: true,
                    out target);
        }
    }

    private bool TryFindNearbyAlly(
        EntityUid uid,
        float radius,
        bool requireDamage,
        bool requireRepair,
        out EntityUid target)
    {
        target = default;
        var origin = _transform.GetWorldPosition(uid);
        var bestDistance = float.MaxValue;
        var scanned = 0;
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            Transform(uid).Coordinates,
            Math.Clamp(radius, CMUUtilityDroneLimits.MinRadius, CMUUtilityDroneLimits.MaxRadius),
            _lookupBuffer,
            LookupFlags.Uncontained);

        foreach (var candidate in _lookupBuffer)
        {
            if (scanned++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;

            if (candidate == uid ||
                TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !TryComp<MobStateComponent>(candidate, out var mob) ||
                _mobState.IsDead(candidate, mob) ||
                !SameZNetwork(uid, candidate) ||
                IsHostileTo(uid, candidate))
            {
                continue;
            }

            if (requireDamage && !CanRepairTarget(candidate))
                continue;

            if (requireRepair && !HasComp<SynthComponent>(candidate))
                continue;

            var distance = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(candidate));
            if (distance >= bestDistance)
                continue;

            target = candidate;
            bestDistance = distance;
        }

        return target.IsValid();
    }

    private bool CanRepairTarget(EntityUid target)
    {
        if (!target.IsValid() ||
            TerminatingOrDeleted(target) ||
            !TryComp<DamageableComponent>(target, out _) ||
            !TryComp<SynthComponent>(target, out var synth) ||
            GetTotalDamage(target) <= 0)
        {
            return false;
        }

        return HasDamageGroup(target, synth.WelderDamageGroup) ||
               HasDamageGroup(target, synth.CableCoilDamageGroup);
    }

    private bool HasNearbyHostiles(EntityUid uid, float radius)
    {
        if (!HasComp<NpcFactionMemberComponent>(uid))
            return false;

        var scanned = 0;
        foreach (var candidate in _faction.GetNearbyHostiles(uid, radius))
        {
            if (scanned++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;

            if (IsValidHostileTarget(uid, candidate))
                return true;
        }

        return false;
    }

    private bool TryGetScriptRepairTool(
        EntityUid uid,
        EntityUid target,
        SynthComponent synth,
        out EntityUid tool,
        out bool cable)
    {
        tool = default;
        cable = !HasDamageGroup(target, synth.WelderDamageGroup) &&
                HasDamageGroup(target, synth.CableCoilDamageGroup);

        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!IsScriptRepairTool(held, synth, cable))
                continue;

            tool = held;
            return true;
        }

        if (!TryGetBackpack(uid, out var backpack))
            return false;

        foreach (var candidate in EnumerateBackpackItems(backpack).ToList())
        {
            if (TerminatingOrDeleted(candidate) ||
                !IsScriptRepairTool(candidate, synth, cable))
            {
                continue;
            }

            if (!RemoveFromBackpack(backpack, candidate))
                continue;

            if (_hands.TryPickupAnyHand(uid, candidate))
            {
                tool = candidate;
                return true;
            }

            InsertBackpackItem(backpack, candidate, uid);
        }

        return false;
    }

    private bool IsScriptRepairTool(
        EntityUid item,
        SynthComponent synth,
        bool cable)
    {
        if (cable)
        {
            return HasComp<RMCCableCoilComponent>(item) &&
                   TryComp<StackComponent>(item, out var stack) &&
                   stack.Count > 0;
        }

        return HasComp<BlowtorchComponent>(item) &&
               _tool.HasQuality(item, synth.RepairQuality);
    }

    private double GetTotalDamage(EntityUid target)
    {
        return TryComp<DamageableComponent>(target, out var damageable)
            ? _damageable.GetTotalDamage((target, damageable)).Double()
            : 0;
    }

    private double GetMaxHealth(EntityUid target)
    {
        if (TryComp<MobThresholdsComponent>(target, out var thresholds) &&
            thresholds.Thresholds.Count > 0)
        {
            return thresholds.Thresholds.Keys.Last().Double();
        }

        return -1;
    }

    #endregion
}
