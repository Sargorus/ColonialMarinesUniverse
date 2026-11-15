using System;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.HiveLeader;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.ColonyEconomy;

// Corrupted-hive representative and alliance checks.
public sealed partial class CorporateConsoleSystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private SharedXenoHiveSystem _xenoHive = default!;

    private bool HasCorruptedHiveRepresentative()
    {
        var hiveQuery = EntityQueryEnumerator<HiveComponent>();
        while (hiveQuery.MoveNext(out var hiveUid, out var hive))
        {
            if (!hive.Corrupted)
                continue;

            if (hive.CurrentQueen is { } queen
                && !TerminatingOrDeleted(queen)
                && _mobState.IsAlive(queen))
            {
                return true;
            }

            var memberQuery = EntityQueryEnumerator<HiveMemberComponent, MobStateComponent>();
            while (memberQuery.MoveNext(out var memberUid, out var member, out _))
            {
                if (member.Hive != hiveUid || !_mobState.IsAlive(memberUid))
                    continue;

                if (HasComp<HiveLeaderComponent>(memberUid))
                    return true;

                var prototype = MetaData(memberUid).EntityPrototype?.ID;
                if (prototype != null
                    && prototype.Contains("Overmind", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasCorruptedHiveAlliance()
    {
        var ally = new ProtoId<NpcFactionPrototype>("AUWeYu");
        var hiveQuery = EntityQueryEnumerator<HiveComponent>();
        while (hiveQuery.MoveNext(out var hiveUid, out var hive))
        {
            if (hive.Corrupted && _xenoHive.HasFaction(hiveUid, ally))
                return true;
        }

        return _npcFaction.IsFactionFriendly("AUWeYu", "CMU14PathogenWalker")
               || _npcFaction.IsFactionFriendly("AUWeYu", "CMUCorruptedHive");
    }
}
