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
    private static CMUUtilityScriptValue NativeMarker(
        CMUUtilityScriptFrame frame,
        CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1)
            return CMUUtilityScriptValue.Null;

        var index = (int) args[0].Number - 1;
        return index >= 0 && index < frame.Markers.Count
            ? CMUUtilityScriptValue.FromEntity(frame.Markers[index])
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeHand(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1)
            return CMUUtilityScriptValue.Null;

        return TryGetHandItem(uid, args[0].Text, out var item)
            ? CMUUtilityScriptValue.FromEntity(item)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeEquipped(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0].AsText))
            return CMUUtilityScriptValue.Null;

        return _inventory.TryGetSlotEntity(uid, args[0].AsText, out var item) &&
               item is { } resolved &&
               !TerminatingOrDeleted(resolved)
            ? CMUUtilityScriptValue.FromEntity(resolved)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeHands(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handId in _hands.EnumerateHands(target))
        {
            if (string.IsNullOrWhiteSpace(handId) || !seen.Add(handId))
                continue;

            var label = handId.StartsWith("hand_", StringComparison.OrdinalIgnoreCase)
                ? handId["hand_".Length..]
                : handId;
            var item = _hands.TryGetHeldItem(target, handId, out var held) &&
                       held is { } heldItem &&
                       !TerminatingOrDeleted(heldItem)
                ? MetaData(heldItem).EntityName
                : Loc.GetString("cmu-utility-drone-script-info-empty-hand");
            entries.Add(Loc.GetString(
                "cmu-utility-drone-script-info-hand",
                ("hand", label),
                ("item", item)));
        }

        return CMUUtilityScriptValue.FromString(FormatEntries(entries));
    }

    private CMUUtilityScriptValue NativeBackpack(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        return TryGetBackpack(target, out var backpack)
            ? CMUUtilityScriptValue.FromEntity(backpack)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeWorn(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!TryComp<InventoryComponent>(target, out var inventory))
            return CMUUtilityScriptValue.FromString(
                Loc.GetString("cmu-utility-drone-script-info-none"));

        var entries = new List<string>();
        var slots = _inventory.GetSlotEnumerator((target, inventory));
        while (slots.NextItem(out var item, out var slot))
        {
            if (TerminatingOrDeleted(item))
                continue;

            entries.Add(Loc.GetString(
                "cmu-utility-drone-script-info-worn",
                ("slot", slot.Name),
                ("item", MetaData(item).EntityName)));
        }

        return CMUUtilityScriptValue.FromString(FormatEntries(entries));
    }

    private CMUUtilityScriptValue NativeBackpackNames(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!TryGetBackpack(target, out var backpack))
            return CMUUtilityScriptValue.FromString(
                Loc.GetString("cmu-utility-drone-script-info-none"));

        var names = EnumerateBackpackItems(backpack)
            .Where(item => !TerminatingOrDeleted(item))
            .Select(item => MetaData(item).EntityName)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return CMUUtilityScriptValue.FromString(FormatNameList(names));
    }

    private CMUUtilityScriptValue NativeBackpackItems(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!TryGetBackpack(target, out var backpack))
            return CMUUtilityScriptValue.FromString(
                Loc.GetString("cmu-utility-drone-script-info-none"));

        var groups = EnumerateBackpackItems(backpack)
            .Where(item => !TerminatingOrDeleted(item))
            .GroupBy(item => MetaData(item).EntityName)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (groups.Count == 0)
            return CMUUtilityScriptValue.FromString(
                Loc.GetString("cmu-utility-drone-script-info-none"));

        var entries = new List<string>();
        foreach (var group in groups.Take(CMUUtilityDroneLimits.MaxInfoEntries))
        {
            var count = group.Count();
            entries.Add(count > 1
                ? Loc.GetString(
                    "cmu-utility-drone-script-info-count",
                    ("name", group.Key),
                    ("count", count))
                : group.Key);
        }

        if (groups.Count > CMUUtilityDroneLimits.MaxInfoEntries)
            entries.Add(Loc.GetString("cmu-utility-drone-script-info-more"));

        return CMUUtilityScriptValue.FromString(TruncateInfo(string.Join(", ", entries)));
    }

    private CMUUtilityScriptValue NativePerson(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0].AsText))
            return CMUUtilityScriptValue.Null;

        var radius = GetInfoRadius(args, 1, 8f);
        return TryFindPerson(uid, args[0].AsText, radius, out var person)
            ? CMUUtilityScriptValue.FromEntity(person)
            : CMUUtilityScriptValue.Null;
    }

    private bool TryFindPerson(
        EntityUid uid,
        string query,
        float radius,
        out EntityUid person)
    {
        person = default;
        foreach (var nearby in GetNearbyInfo(uid, radius, CMUUtilityNearbyKind.People))
        {
            if (!MatchesName(nearby.Entity, query))
                continue;

            person = nearby.Entity;
            return true;
        }

        return false;
    }

    private CMUUtilityScriptValue NativeNearestPerson(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        var nearby = GetNearbyInfo(uid, radius, CMUUtilityNearbyKind.People);
        return nearby.Count == 0
            ? CMUUtilityScriptValue.Null
            : CMUUtilityScriptValue.FromEntity(nearby[0].Entity);
    }

    private CMUUtilityScriptValue NativePeopleNames(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        var names = GetNearbyInfo(uid, radius, CMUUtilityNearbyKind.People)
            .Select(person => person.Name)
            .ToList();
        return CMUUtilityScriptValue.FromString(FormatNameList(names));
    }

    private CMUUtilityScriptValue NativeInspectPerson(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        var target = GetInfoTarget(uid, args);
        if (!target.IsValid() || TerminatingOrDeleted(target))
            return CMUUtilityScriptValue.Null;

        var empty = Array.Empty<CMUUtilityScriptValue>();
        var hands = NativeHands(target, empty).AsText;
        var worn = NativeWorn(target, empty).AsText;
        var backpack = NativeBackpackNames(target, empty).AsText;
        return CMUUtilityScriptValue.FromString(TruncateInfo(Loc.GetString(
            "cmu-utility-drone-script-info-inspect",
            ("name", MetaData(target).EntityName),
            ("hands", hands),
            ("worn", worn),
            ("backpack", backpack))));
    }

    private CMUUtilityScriptValue NativeWornContains(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        if (args.Length < 2)
            return CMUUtilityScriptValue.FromBool(false);

        var target = GetInfoTarget(uid, args);
        var query = args[1].AsText.Trim();
        if (query.Length == 0)
            return CMUUtilityScriptValue.FromBool(false);

        foreach (var item in EnumerateWornItems(target))
        {
            var metadata = MetaData(item);
            if (metadata.EntityName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                metadata.EntityPrototype?.ID.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            {
                return CMUUtilityScriptValue.FromBool(true);
            }
        }

        return CMUUtilityScriptValue.FromBool(false);
    }

    private EntityUid GetInfoTarget(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        if (args.Length > 0 &&
            args[0].Type == CMUUtilityScriptValueType.Entity &&
            args[0].Entity.IsValid() &&
            !TerminatingOrDeleted(args[0].Entity))
        {
            return args[0].Entity;
        }

        return uid;
    }

    private IEnumerable<EntityUid> EnumerateWornItems(EntityUid uid)
    {
        if (!TryComp<InventoryComponent>(uid, out var inventory))
            yield break;

        var slots = _inventory.GetSlotEnumerator((uid, inventory));
        while (slots.NextItem(out var item))
        {
            if (!TerminatingOrDeleted(item))
                yield return item;
        }
    }

    private CMUUtilityScriptValue NativeNearbyInfo(
        EntityUid uid,
        CMUUtilityScriptValue[] args,
        CMUUtilityNearbyKind kind)
    {
        var radius = GetInfoRadius(args, 0, 8f);
        var nearby = GetNearbyInfo(uid, radius, kind);
        if (nearby.Count == 0)
            return CMUUtilityScriptValue.FromString(
                Loc.GetString("cmu-utility-drone-script-info-none"));

        var entries = new List<string>();
        foreach (var entry in nearby.Take(CMUUtilityDroneLimits.MaxInfoEntries))
        {
            entries.Add(Loc.GetString(
                "cmu-utility-drone-script-info-distance",
                ("name", entry.Name),
                ("distance", entry.Distance.ToString("0.#", CultureInfo.InvariantCulture))));
        }

        if (nearby.Count > CMUUtilityDroneLimits.MaxInfoEntries)
            entries.Add(Loc.GetString("cmu-utility-drone-script-info-more"));

        return CMUUtilityScriptValue.FromString(TruncateInfo(string.Join(", ", entries)));
    }

    private CMUUtilityScriptValue NativeDamage(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var target = uid;
        if (args.Length > 0)
        {
            if (args[0].Type != CMUUtilityScriptValueType.Entity ||
                !args[0].Entity.IsValid() ||
                TerminatingOrDeleted(args[0].Entity))
            {
                return CMUUtilityScriptValue.FromNumber(-1);
            }

            target = args[0].Entity;
        }

        if (!TryComp<DamageableComponent>(target, out var damageable))
            return CMUUtilityScriptValue.FromNumber(0);

        return CMUUtilityScriptValue.FromNumber(
            _damageable.GetTotalDamage((target, damageable)).Double());
    }

    private CMUUtilityScriptValue NativePulling(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var user = uid;
        if (args.Length > 0)
        {
            if (args[0].Type != CMUUtilityScriptValueType.Entity ||
                !args[0].Entity.IsValid() ||
                TerminatingOrDeleted(args[0].Entity))
            {
                return CMUUtilityScriptValue.Null;
            }

            user = args[0].Entity;
        }

        var pulled = _pulling.GetPulling(user);
        return pulled is { } pulledEntity && !TerminatingOrDeleted(pulledEntity)
            ? CMUUtilityScriptValue.FromEntity(pulledEntity)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeAimTarget(EntityUid uid, CMUUtilityScriptValue[] args)
    {
        var user = uid;
        if (args.Length > 0)
        {
            if (args[0].Type != CMUUtilityScriptValueType.Entity ||
                !args[0].Entity.IsValid() ||
                TerminatingOrDeleted(args[0].Entity))
            {
                return CMUUtilityScriptValue.Null;
            }

            user = args[0].Entity;
        }

        if (TryComp<CMUUtilityDroneAgentComponent>(user, out var agent))
        {
            return agent.CombatTarget is { } scriptTarget &&
                   scriptTarget.IsValid() &&
                   !TerminatingOrDeleted(scriptTarget)
                ? CMUUtilityScriptValue.FromEntity(scriptTarget)
                : CMUUtilityScriptValue.Null;
        }

        return TryGetAimTarget(user, out var target)
            ? CMUUtilityScriptValue.FromEntity(target)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeExists(CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1)
            return CMUUtilityScriptValue.FromBool(false);

        var value = args[0];
        if (value.Type == CMUUtilityScriptValueType.Entity)
        {
            return CMUUtilityScriptValue.FromBool(
                value.Entity.IsValid() &&
                !TerminatingOrDeleted(value.Entity));
        }

        return CMUUtilityScriptValue.FromBool(value.IsTruthy);
    }

    private CMUUtilityScriptValue NativeAttacker(
        EntityUid uid,
        CMUUtilityScriptFrame frame,
        CMUUtilityScriptValue[] args)
    {
        if (frame.EventContext)
        {
            return frame.EventSubject is { } eventSubject &&
                   eventSubject.IsValid() &&
                   !TerminatingOrDeleted(eventSubject)
                ? CMUUtilityScriptValue.FromEntity(eventSubject)
                : CMUUtilityScriptValue.Null;
        }

        if (!TryGetAgentTarget(uid, args, out var agent))
            return CMUUtilityScriptValue.Null;

        return agent.LastAttacker is { } attacker &&
               attacker.IsValid() &&
               !TerminatingOrDeleted(attacker)
            ? CMUUtilityScriptValue.FromEntity(attacker)
            : CMUUtilityScriptValue.Null;
    }

    private CMUUtilityScriptValue NativeDamageTaken(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        if (!TryGetAgentTarget(uid, args, out var agent))
            return CMUUtilityScriptValue.FromBool(false);

        var pending = agent.DamagePending;
        agent.DamagePending = false;
        return CMUUtilityScriptValue.FromBool(pending);
    }

    private CMUUtilityScriptValue NativeBackpackFits(
        EntityUid uid,
        CMUUtilityScriptValue[] args)
    {
        if (args.Length < 1 ||
            !TryResolveItemValue(uid, args[0], out var item) ||
            !TryGetBackpack(uid, out var backpack))
        {
            return CMUUtilityScriptValue.FromBool(false);
        }

        if (TryComp<StorageComponent>(backpack, out var storage))
        {
            return CMUUtilityScriptValue.FromBool(
                _storage.CanInsert(backpack, item, out _, storage));
        }

        return CMUUtilityScriptValue.FromBool(
            TryComp<EntityStorageComponent>(backpack, out var entityStorage) &&
            _entityStorage.CanInsert(item, backpack, entityStorage));
    }

    private bool TryGetAgentTarget(
        EntityUid uid,
        CMUUtilityScriptValue[] args,
        out CMUUtilityDroneAgentComponent agent)
    {
        agent = default!;
        var target = uid;
        if (args.Length > 0)
        {
            if (args[0].Type != CMUUtilityScriptValueType.Entity ||
                !args[0].Entity.IsValid() ||
                TerminatingOrDeleted(args[0].Entity))
            {
                return false;
            }

            target = args[0].Entity;
        }

        if (!TryComp<CMUUtilityDroneAgentComponent>(target, out var found) ||
            found == null)
        {
            return false;
        }

        agent = found;
        return true;
    }

    private bool TryGetAimTarget(EntityUid user, out EntityUid target)
    {
        target = default;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryGetGunTarget(held, out target) ||
                TryGetTargetingTarget(held, out target))
            {
                return true;
            }
        }

        if (TryComp<InventoryComponent>(user, out var inventory))
        {
            var slots = _inventory.GetSlotEnumerator((user, inventory));
            while (slots.NextItem(out var item))
            {
                if (TryGetGunTarget(item, out target) ||
                    TryGetTargetingTarget(item, out target))
                {
                    return true;
                }
            }
        }

        if (TryGetTargetingTarget(user, out target))
            return true;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp<GunComponent>(held, out var gun) &&
                gun.ShootCoordinates is { } shootCoordinates &&
                TryFindEntityAtCoordinates(shootCoordinates, user, out target))
            {
                return true;
            }
        }

        if (TryComp<InventoryComponent>(user, out inventory))
        {
            var slots = _inventory.GetSlotEnumerator((user, inventory));
            while (slots.NextItem(out var item))
            {
                if (TryComp<GunComponent>(item, out var gun) &&
                    gun.ShootCoordinates is { } shootCoordinates &&
                    TryFindEntityAtCoordinates(shootCoordinates, user, out target))
                {
                    return true;
                }
            }
        }

        return TryScanAimCone(user, out target);
    }

    private bool TryGetGunTarget(EntityUid item, out EntityUid target)
    {
        target = default;
        if (!TryComp<GunComponent>(item, out var gun) ||
            gun.Target is not { } candidate ||
            TerminatingOrDeleted(candidate))
        {
            return false;
        }

        target = candidate;
        return true;
    }

    private bool TryGetTargetingTarget(EntityUid item, out EntityUid target)
    {
        target = default;
        if (!TryComp<TargetingComponent>(item, out var targeting))
            return false;

        foreach (var candidate in targeting.Targets)
        {
            if (!candidate.IsValid() ||
                candidate == item ||
                TerminatingOrDeleted(candidate))
            {
                continue;
            }

            target = candidate;
            return true;
        }

        return false;
    }

    private bool TryFindEntityAtCoordinates(
        EntityCoordinates coordinates,
        EntityUid user,
        out EntityUid target)
    {
        target = default;
        if (!coordinates.IsValid(EntityManager))
            return false;

        var mapCoordinates = _transform.ToMapCoordinates(coordinates);
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(coordinates, 1.25f, _lookupBuffer, LookupFlags.Uncontained);

        var bestDistance = float.MaxValue;
        var candidates = 0;
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxInfoCandidates)
                break;

            if (candidate == user ||
                TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !HasComp<MobStateComponent>(candidate) && !HasComp<ItemComponent>(candidate))
            {
                continue;
            }

            var distance = System.Numerics.Vector2.DistanceSquared(
                mapCoordinates.Position,
                _transform.GetWorldPosition(candidate));
            if (distance >= bestDistance)
                continue;

            target = candidate;
            bestDistance = distance;
        }

        return target.IsValid();
    }

    private bool TryScanAimCone(EntityUid user, out EntityUid target)
    {
        target = default;
        var xform = Transform(user);
        var forward = _transform.GetWorldRotation(xform).ToWorldVec();
        if (forward.LengthSquared() < 0.0001f)
            return false;

        forward = System.Numerics.Vector2.Normalize(forward);
        var origin = _transform.GetWorldPosition(xform);
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, 12f, _lookupBuffer, LookupFlags.Uncontained);

        var bestScore = float.MaxValue;
        var candidates = 0;
        foreach (var candidate in _lookupBuffer)
        {
            if (candidates++ >= CMUUtilityDroneLimits.MaxInfoCandidates)
                break;

            if (candidate == user ||
                TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate) ||
                !HasComp<MobStateComponent>(candidate) &&
                (!HasComp<ItemComponent>(candidate) || Transform(candidate).Anchored))
            {
                continue;
            }

            var offset = _transform.GetWorldPosition(candidate) - origin;
            var distance = offset.Length();
            if (distance < 0.25f || distance > 12f)
                continue;

            var dot = System.Numerics.Vector2.Dot(forward, offset / distance);
            if (dot < 0.8f)
                continue;

            var score = distance * (2f - dot);
            if (score >= bestScore)
                continue;

            target = candidate;
            bestScore = score;
        }

        return target.IsValid();
    }

    private List<CMUUtilityNearbyInfo> GetNearbyInfo(
        EntityUid uid,
        float radius,
        CMUUtilityNearbyKind kind)
    {
        var result = new List<CMUUtilityNearbyInfo>();
        radius = Math.Clamp(radius, 0.5f, CMUUtilityDroneLimits.MaxRadius);
        var origin = _transform.GetWorldPosition(uid);
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            Transform(uid).Coordinates,
            radius,
            _lookupBuffer,
            LookupFlags.Uncontained);

        var scanned = 0;
        foreach (var candidate in _lookupBuffer)
        {
            if (scanned++ >= CMUUtilityDroneLimits.MaxInfoCandidates * 4 ||
                result.Count >= CMUUtilityDroneLimits.MaxInfoCandidates)
            {
                break;
            }

            if (candidate == uid ||
                TerminatingOrDeleted(candidate) ||
                HasComp<CMUUtilityDroneAgentComponent>(candidate) ||
                HasComp<CMUUtilityTaskMarkerComponent>(candidate))
            {
                continue;
            }

            var candidateXform = Transform(candidate);
            var alive = TryComp<MobStateComponent>(candidate, out var mob) &&
                        _mobState.IsAlive(candidate, mob);
            var item = HasComp<ItemComponent>(candidate) && !candidateXform.Anchored;
            var include = kind switch
            {
                CMUUtilityNearbyKind.People => alive,
                CMUUtilityNearbyKind.Items => item,
                CMUUtilityNearbyKind.Entities => alive ||
                                                  item ||
                                                  (!candidateXform.Anchored &&
                                                   HasComp<PhysicsComponent>(candidate)),
                _ => false,
            };
            if (!include)
                continue;

            result.Add(new CMUUtilityNearbyInfo(
                candidate,
                MetaData(candidate).EntityName,
                System.Numerics.Vector2.Distance(origin, _transform.GetWorldPosition(candidate))));
        }

        result.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        return result;
    }

    private static float GetInfoRadius(
        CMUUtilityScriptValue[] args,
        int index,
        float fallback)
    {
        if (args.Length <= index ||
            args[index].Type != CMUUtilityScriptValueType.Number ||
            !double.IsFinite(args[index].Number))
        {
            return fallback;
        }

        return Math.Clamp(
            (float) args[index].Number,
            0.5f,
            CMUUtilityDroneLimits.MaxRadius);
    }

    private string FormatEntries(List<string> entries)
    {
        if (entries.Count == 0)
            return Loc.GetString("cmu-utility-drone-script-info-none");

        var shown = entries.Take(CMUUtilityDroneLimits.MaxInfoEntries).ToList();
        if (entries.Count > CMUUtilityDroneLimits.MaxInfoEntries)
            shown.Add(Loc.GetString("cmu-utility-drone-script-info-more"));

        return TruncateInfo(string.Join("; ", shown));
    }

    private string FormatNameList(List<string> names)
    {
        if (names.Count == 0)
            return Loc.GetString("cmu-utility-drone-script-info-none");

        var entries = names.Take(CMUUtilityDroneLimits.MaxInfoEntries).ToList();
        if (names.Count > CMUUtilityDroneLimits.MaxInfoEntries)
            entries.Add(Loc.GetString("cmu-utility-drone-script-info-more"));

        return TruncateInfo(string.Join(", ", entries));
    }

    private static string TruncateInfo(string text)
    {
        return text.Length <= CMUUtilityDroneLimits.MaxInfoTextLength
            ? text
            : text[..(CMUUtilityDroneLimits.MaxInfoTextLength - 1)] + "…";
    }

    private enum CMUUtilityNearbyKind : byte
    {
        People,
        Items,
        Entities,
    }

    private readonly record struct CMUUtilityNearbyInfo(
        EntityUid Entity,
        string Name,
        float Distance);

}