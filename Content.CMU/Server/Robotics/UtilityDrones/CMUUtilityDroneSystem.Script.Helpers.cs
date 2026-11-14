using System.Globalization;
using System.Linq;
using Content.Shared.Chat;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Damage.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.NPC;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._RMC14.Targeting;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    private CMUUtilityScriptValue NativeBackpackItem(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1 || !TryGetBackpack(uid, out var backpack))
            return CMUUtilityScriptValue.Null;

        return TryFindBackpackItem(backpack, args[0].AsText, out var item)
            ? CMUUtilityScriptValue.FromEntity(item)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeItem(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1)
            return CMUUtilityScriptValue.Null;

        var radius = args.Length > 1 ? (float) args[1].Number : 8f;
        return TryFindWorldItem(uid, args[0].AsText, radius, out var item)
            ? CMUUtilityScriptValue.FromEntity(item)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeName(CMUUtilityScriptValue[] args)
    {
        return args.Length > 0 && args[0].Type == CMUUtilityScriptValueType.Entity &&
               !TerminatingOrDeleted(args[0].Entity)
            ? CMUUtilityScriptValue.FromString(MetaData(args[0].Entity).EntityName)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeItemId(CMUUtilityScriptValue[] args)
    {
        return args.Length > 0 && args[0].Type == CMUUtilityScriptValueType.Entity &&
               !TerminatingOrDeleted(args[0].Entity)
            ? CMUUtilityScriptValue.FromString(
                MetaData(args[0].Entity).EntityPrototype?.ID ?? string.Empty)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeItemDisplay(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0].AsText))
            return CMUUtilityScriptValue.Null;

        var radius = args.Length > 1 ? (float) args[1].Number : 8f;
        return TryFindWorldItem(
            uid,
            args[0].AsText,
            radius,
            out var item,
            displayNameOnly: true)
            ? CMUUtilityScriptValue.FromEntity(item)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeHandName(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1)
            return CMUUtilityScriptValue.Null;

        return TryGetHandItem(uid, args[0].AsText, out var item)
            ? CMUUtilityScriptValue.FromString(MetaData(item).EntityName)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeInHand(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1)
            return CMUUtilityScriptValue.FromBool(false);

        return CMUUtilityScriptValue.FromBool(TryGetHandItem(uid, args[0].AsText, out _));
    }

    private CMUUtilityScriptValue NativeHasBackpackItem(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1 || !TryGetBackpack(uid, out var backpack))
            return CMUUtilityScriptValue.FromBool(false);

        return CMUUtilityScriptValue.FromBool(
            TryFindBackpackItem(backpack, args[0].AsText, out _));
    }

    private CMUUtilityScriptValue NativeDistance(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1 ||
            args[0].Type != CMUUtilityScriptValueType.Entity ||
            TerminatingOrDeleted(args[0].Entity) ||
            Transform(uid).MapID != Transform(args[0].Entity).MapID)
        {
            return CMUUtilityScriptValue.FromNumber(-1);
        }

        return CMUUtilityScriptValue.FromNumber(
            System.Numerics.Vector2.Distance(
                _transform.GetWorldPosition(uid),
                _transform.GetWorldPosition(args[0].Entity)));
    }

    private bool TryGetHandItem(EntityUid uid, string side, out EntityUid item)
    {
        item = default;
        if (!TryResolveHandId(uid, side, out var hand))
            return false;

        return _hands.TryGetHeldItem(uid, hand, out var held) && held is { } resolved
            ? (item = resolved).IsValid()
            : false;
    }

    private void SwitchHand(EntityUid uid, string side)
    {
        if (TryResolveHandId(uid, side, out var hand))
            _hands.TrySetActiveHand(uid, hand);
    }

    /// <summary>
    /// Body organs create the normal <c>left</c>/<c>right</c> hands. Older
    /// utility-drone prototypes also added <c>hand_left</c>/<c>hand_right</c>
    /// starting hands, so accept both IDs while existing maps migrate.
    /// </summary>
    private bool TryResolveHandId(EntityUid uid, string side, out string handId)
    {
        handId = string.Empty;
        var primary = side.Equals("left", StringComparison.OrdinalIgnoreCase)
            ? "left"
            : side.Equals("right", StringComparison.OrdinalIgnoreCase)
                ? "right"
                : string.Empty;
        if (primary.Length == 0)
            return false;

        if (_hands.TryGetHand(uid, primary, out _))
        {
            handId = primary;
            return true;
        }

        var legacy = primary == "left" ? "hand_left" : "hand_right";
        if (_hands.TryGetHand(uid, legacy, out _))
        {
            handId = legacy;
            return true;
        }

        return false;
    }

    private bool TryGetBackpack(EntityUid uid, out EntityUid backpack)
    {
        backpack = default;
        if (!_inventory.TryGetSlotEntity(uid, "back", out var item) ||
            item is not { } resolved ||
            TerminatingOrDeleted(resolved))
        {
            return false;
        }

        backpack = resolved;
        return true;
    }

    private bool TryFindBackpackItem(EntityUid backpack, string name, out EntityUid item)
    {
        item = default;
        var bestLength = int.MaxValue;
        foreach (var candidate in EnumerateBackpackItems(backpack))
        {
            if (TerminatingOrDeleted(candidate) ||
                !MatchesName(candidate, name))
            {
                continue;
            }

            var length = MetaData(candidate).EntityName.Length;
            if (length >= bestLength)
                continue;

            item = candidate;
            bestLength = length;
        }

        return item.IsValid();
    }

    private bool MatchesName(EntityUid uid, string query, bool displayNameOnly = false)
    {
        var metadata = MetaData(uid);
        if (CMUUtilityScriptNameMatcher.Matches(metadata.EntityName, query))
            return true;

        var prototype = metadata.EntityPrototype;
        if (prototype == null)
            return false;

        if (!displayNameOnly &&
            CMUUtilityScriptNameMatcher.Matches(prototype.ID, query))
        {
            return true;
        }

        // Some fork locale files use ent-{id}-name while the engine resolves
        // ent-{id}. Check the localized display name explicitly so Russian
        // queries match even when EntityName fell back to the YAML name.
        return _loc.TryGetString($"ent-{prototype.ID}-name", out var localizedName) &&
               CMUUtilityScriptNameMatcher.Matches(localizedName, query);
    }

    private bool TryResolveItemValue(
        EntityUid uid,
        CMUUtilityScriptValue value,
        out EntityUid entity)
    {
        entity = default;
        if (value.Type == CMUUtilityScriptValueType.Entity &&
            value.Entity.IsValid() &&
            !TerminatingOrDeleted(value.Entity))
        {
            entity = value.Entity;
            return true;
        }

        if (value.Type != CMUUtilityScriptValueType.String ||
            string.IsNullOrWhiteSpace(value.Text))
        {
            return false;
        }

        return TryFindWorldItem(uid, value.Text, 8f, out entity);
    }

    private bool TryResolveInteractionTarget(
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

        return TryFindWorldItem(uid, value.Text, 8f, out target) ||
               TryFindPerson(uid, value.Text, 8f, out target);
    }

    private bool TryResolveHeldItem(
        EntityUid uid,
        CMUUtilityScriptValue value,
        out EntityUid item)
    {
        item = default;
        if (value.Type == CMUUtilityScriptValueType.Entity &&
            value.Entity.IsValid() &&
            !TerminatingOrDeleted(value.Entity) &&
            _hands.EnumerateHeld(uid).Contains(value.Entity))
        {
            item = value.Entity;
            return true;
        }

        if (value.Type != CMUUtilityScriptValueType.String ||
            string.IsNullOrWhiteSpace(value.Text))
        {
            return false;
        }

        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!MatchesName(held, value.Text))
                continue;

            item = held;
            return true;
        }

        return false;
    }

    private IEnumerable<EntityUid> EnumerateBackpackItems(EntityUid backpack)
    {
        if (TryComp<StorageComponent>(backpack, out var storage))
        {
            foreach (var item in storage.Container.ContainedEntities)
                yield return item;
        }

        if (TryComp<EntityStorageComponent>(backpack, out var entityStorage))
        {
            foreach (var item in entityStorage.Contents.ContainedEntities)
                yield return item;
        }
    }

    private bool RemoveFromBackpack(EntityUid backpack, EntityUid item)
    {
        if (TryComp<StorageComponent>(backpack, out var storage) &&
            storage.Container.ContainedEntities.Contains(item))
        {
            return _containers.Remove(item, storage.Container, force: true);
        }

        return TryComp<EntityStorageComponent>(backpack, out var entityStorage) &&
               _entityStorage.Remove(item, backpack, entityStorage);
    }

    private bool InsertBackpackItem(EntityUid backpack, EntityUid item, EntityUid user)
    {
        if (TryComp<StorageComponent>(backpack, out _))
            return _storage.Insert(backpack, item, out _, out _, user);

        return TryComp<EntityStorageComponent>(backpack, out var entityStorage) &&
               _entityStorage.Insert(item, backpack, entityStorage);
    }

    private int CountBackpackItems(EntityUid uid)
    {
        return TryGetBackpack(uid, out var backpack)
            ? EnumerateBackpackItems(backpack).Count()
            : 0;
    }

    private int CountPeople(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, radius, _lookupBuffer, LookupFlags.Uncontained);

        var count = 0;
        var candidates = 0;
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;

            if (candidate == uid ||
                TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                !TryComp<MobStateComponent>(candidate, out var mob) ||
                !_mobState.IsAlive(candidate, mob))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private int CountItems(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, radius, _lookupBuffer, LookupFlags.Uncontained);

        var count = 0;
        var candidates = 0;
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;

            if (candidate == uid ||
                TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !HasComp<ItemComponent>(candidate) ||
                Transform(candidate).Anchored)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private bool TryFindWorldItem(
        EntityUid uid,
        string query,
        float radius,
        out EntityUid item,
        bool displayNameOnly = false)
    {
        item = default;
        radius = Math.Clamp(radius, 0.5f, CMUUtilityDroneLimits.MaxRadius);
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, radius, _lookupBuffer, LookupFlags.Uncontained);

        var bestDistance = float.MaxValue;
        var candidates = 0;
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxScanCandidates)
                break;

            if (candidate == uid ||
                TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !HasComp<ItemComponent>(candidate) ||
                Transform(candidate).Anchored ||
                !MatchesName(candidate, query, displayNameOnly))
            {
                continue;
            }

            var distance = System.Numerics.Vector2.DistanceSquared(
                _transform.GetWorldPosition(uid),
                _transform.GetWorldPosition(candidate));
            if (distance >= bestDistance)
                continue;

            item = candidate;
            bestDistance = distance;
        }

        return item.IsValid();
    }

    private static void Push(CMUUtilityScriptFrame frame, CMUUtilityScriptValue value)
    {
        frame.Push(value);
    }

    private static bool TryPop(CMUUtilityScriptFrame frame, out CMUUtilityScriptValue value)
    {
        return frame.TryPop(out value);
    }

    private static bool TryPopNumber(CMUUtilityScriptFrame frame, out double value)
    {
        return frame.TryPopNumber(out value);
    }

    private static bool TryPopEntity(CMUUtilityScriptFrame frame, out EntityUid entity)
    {
        return frame.TryPopEntity(out entity);
    }

}