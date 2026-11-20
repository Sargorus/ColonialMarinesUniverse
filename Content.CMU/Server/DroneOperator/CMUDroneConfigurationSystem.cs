using System.Linq;
using Content.Server.CMU14.Robotics;
using Content.Server.CMU14.Robotics.UtilityDrones;
using Content.Shared._RMC14.Sentry;
using Content.Shared._RMC14.Weapons.Ranged.IFF;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CMU14.Configuration;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Robotics;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;

namespace Content.Server.CMU14.DroneOperator;

/// <summary>
/// A Weyland-Yutani configuration key that rewrites a drone's faction, radio channels and access tags.
/// </summary>
public sealed partial class CMUDroneConfigurationSystem : EntitySystem
{
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private GunIFFSystem _iff = default!;
    [Dependency] private CMUMedibotConfigurationSystem _medibotConfig = default!;
    [Dependency] private NpcFactionSystem _npcFactions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private CMUUtilityDroneSystem _utilityDrones = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUDroneConfigurationKeyComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ActorComponent, DidEquipHandEvent>(OnDidEquipHand);
        SubscribeLocalEvent<ActorComponent, DidUnequipHandEvent>(OnDidUnequipHand);

        Subs.BuiEvents<CMUDroneAndroidComponent>(CMUDroneConfigurationUi.Key, subs =>
        {
            subs.Event<CMUDroneConfigurationApplyMsg>(OnApply);
            subs.Event<CMUUtilityDroneConfigMsg>(OnUtilityConfig);
        });
    }

    private void OnDidEquipHand(Entity<ActorComponent> ent, ref DidEquipHandEvent args)
        => RefreshOpenSession(ent.Owner);

    private void OnDidUnequipHand(Entity<ActorComponent> ent, ref DidUnequipHandEvent args)
        => RefreshOpenSession(ent.Owner);

    private void RefreshOpenSession(EntityUid user)
    {
        var query = EntityQueryEnumerator<CMUDroneConfigurationSessionComponent>();
        while (query.MoveNext(out var drone, out var session))
        {
            if (session.User != user)
                continue;

            session.AvailableChannels = GetAvailableChannels(user);
            session.HasIdCard = TryFindHeldIdCard(user, session.Key, out var idCard);
            session.AvailableAccess = idCard is { } card && TryComp<AccessComponent>(card, out var access)
                ? new HashSet<ProtoId<AccessLevelPrototype>>(access.Tags)
                : new HashSet<ProtoId<AccessLevelPrototype>>();

            UpdateUi(drone, session);
        }
    }

    private void OnAfterInteract(Entity<CMUDroneConfigurationKeyComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled
            || args.Target is not { } target
            || !IsHolding(args.User, ent.Owner))
            return;

        if (TryComp<CMUMedibotComponent>(target, out var medibot))
        {
            args.Handled = true;
            _medibotConfig.Open((target, medibot), args.User, ent.Owner);
            return;
        }

        if (!HasComp<CMUDroneAndroidComponent>(target))
            return;

        args.Handled = true;

        var user = args.User;
        var session = EnsureComp<CMUDroneConfigurationSessionComponent>(target);
        session.Key = ent.Owner;
        session.User = user;
        session.AvailableChannels = GetAvailableChannels(user);
        session.HasIdCard = TryFindHeldIdCard(user, ent.Owner, out var idCard);
        session.AvailableAccess = idCard is { } card && TryComp<AccessComponent>(card, out var access)
            ? new HashSet<ProtoId<AccessLevelPrototype>>(access.Tags)
            : new HashSet<ProtoId<AccessLevelPrototype>>();

        UpdateUi(target, session);
        _ui.TryOpenUi(target, CMUDroneConfigurationUi.Key, user);
    }

    private void OnApply(Entity<CMUDroneAndroidComponent> ent, ref CMUDroneConfigurationApplyMsg msg)
    {
        if (!TryComp<CMUDroneConfigurationSessionComponent>(ent, out var session)
            || session.User != msg.Actor
            || !IsHolding(session.User, session.Key))
            return;

        if (!_proto.TryIndex<NpcFactionPrototype>(msg.Faction, out var faction)
            || faction.Abstract)
            return;

        _npcFactions.ClearFactions(ent.Owner);
        _npcFactions.AddFaction(ent.Owner, msg.Faction);

        if (TryFindIffFaction(msg.Faction, out var iff))
            _iff.SetUserFaction(ent.Owner, iff);
        else
            _iff.ClearUserFactions(ent.Owner);

        ent.Comp.IffConfigured = true;

        // Re-read the operator's current credentials at apply time so a card or headset
        // dropped after the window opened cannot grant stale channels or access.
        var availableChannels = GetAvailableChannels(session.User);
        session.AvailableChannels = availableChannels;
        session.HasIdCard = TryFindHeldIdCard(session.User, session.Key, out var idCard);
        session.AvailableAccess = idCard is { } card && TryComp<AccessComponent>(card, out var cardAccess)
            ? new HashSet<ProtoId<AccessLevelPrototype>>(cardAccess.Tags)
            : new HashSet<ProtoId<AccessLevelPrototype>>();

        var channels = new HashSet<ProtoId<RadioChannelPrototype>>();
        foreach (var id in msg.Channels)
        {
            if (availableChannels.Contains(id))
                channels.Add(id);
        }

        EnsureComp<IntrinsicRadioReceiverComponent>(ent);
        EnsureComp<IntrinsicRadioTransmitterComponent>(ent).Channels = channels;
        EnsureComp<ActiveRadioComponent>(ent).Channels = channels;

        var access = new HashSet<ProtoId<AccessLevelPrototype>>();
        foreach (var id in msg.Access)
        {
            if (session.AvailableAccess.Contains(id))
                access.Add(id);
        }

        var accessComp = EnsureComp<AccessComponent>(ent);
        _access.TrySetTags(ent.Owner, access, accessComp);

        UpdateUi(ent.Owner, session);
        _popup.PopupEntity(Loc.GetString("cmu-drone-config-applied"), ent, session.User);
    }

    private void OnUtilityConfig(Entity<CMUDroneAndroidComponent> ent, ref CMUUtilityDroneConfigMsg msg)
    {
        if (!TryComp<CMUDroneConfigurationSessionComponent>(ent, out var session) ||
            session.User != msg.Actor ||
            !IsHolding(session.User, session.Key) ||
            !HasComp<CMUUtilityDroneAgentComponent>(ent) ||
            !Enum.IsDefined(msg.Action))
        {
            return;
        }

        string error;
        bool success;
        switch (msg.Action)
        {
            case CMUUtilityDroneConfigAction.SetProgrammableMode:
                success = _utilityDrones.TrySetProgrammableMode(ent.Owner, msg.Enabled, out error);
                break;
            case CMUUtilityDroneConfigAction.SetBoardPortLocked:
                success = _utilityDrones.TrySetBoardPortLocked(ent.Owner, msg.Enabled, out error);
                break;
            case CMUUtilityDroneConfigAction.RestartProgram:
                success = _utilityDrones.TryRestartProgram(ent.Owner, out error);
                break;
            case CMUUtilityDroneConfigAction.PauseProgram:
                success = _utilityDrones.PauseProgram(ent.Owner, out error);
                break;
            case CMUUtilityDroneConfigAction.ResumeProgram:
                success = _utilityDrones.ResumeProgram(ent.Owner, out error);
                break;
            case CMUUtilityDroneConfigAction.StopProgram:
                success = _utilityDrones.StopProgram(ent.Owner, out error);
                break;
            case CMUUtilityDroneConfigAction.EjectProgramBoard:
                success = _utilityDrones.TryEjectProgramBoard(ent.Owner, session.User, out error);
                break;
            default:
                return;
        }

        if (!success && !string.IsNullOrEmpty(error))
            _popup.PopupEntity(error, ent, session.User, PopupType.SmallCaution);
        else if (success)
            _popup.PopupEntity(Loc.GetString("cmu-utility-drone-config-updated"), ent, session.User);

        UpdateUi(ent.Owner, session);
    }

    private void UpdateUi(EntityUid drone, CMUDroneConfigurationSessionComponent session)
    {
        var factions = BuildFactionOptions(session.User);

        var currentFaction = string.Empty;
        if (TryComp<NpcFactionMemberComponent>(drone, out var member))
            currentFaction = member.Factions.FirstOrDefault().Id ?? string.Empty;

        var channels = new List<CMUConfigurationOption>();
        foreach (var channelId in session.AvailableChannels)
        {
            if (!_proto.TryIndex(channelId, out RadioChannelPrototype? channel))
                continue;

            channels.Add(new CMUConfigurationOption(channelId.Id, channel.LocalizedName));
        }
        channels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        var selectedChannels = new HashSet<string>();
        if (TryComp<ActiveRadioComponent>(drone, out var active))
            selectedChannels.UnionWith(active.Channels.Select(channel => channel.Id));
        if (TryComp<IntrinsicRadioTransmitterComponent>(drone, out var transmitter))
            selectedChannels.UnionWith(transmitter.Channels.Select(channel => channel.Id));

        var access = new List<CMUConfigurationOption>();
        foreach (var accessId in session.AvailableAccess)
        {
            if (!_proto.TryIndex(accessId, out AccessLevelPrototype? level))
                continue;

            access.Add(new CMUConfigurationOption(accessId.Id, level.GetAccessLevelName()));
        }
        access.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        var selectedAccess = new HashSet<string>();
        if (TryComp<AccessComponent>(drone, out var accessComp))
            selectedAccess.UnionWith(accessComp.Tags.Select(tag => tag.Id));

        var utilityDrone = TryComp<CMUUtilityDroneAgentComponent>(drone, out var utility);
        var step = utility != null && (utility.ProgramRunning || utility.ForcedReturn)
            ? utility.StepIndex + 1
            : 0;

        var state = new CMUDroneConfigurationBuiState(
            factions,
            currentFaction,
            channels,
            selectedChannels,
            access,
            selectedAccess,
            session.HasIdCard,
            utilityDrone,
            utility?.ProgrammableMode ?? false,
            utility?.BoardPortLocked ?? true,
            utility?.ProgramBoard != null,
            utility?.ProgramRunning ?? false,
            utility?.ProgramPaused ?? false,
            step,
            utility?.Program.Steps.Count ?? 0,
            utility?.Activity ?? string.Empty);

        _ui.SetUiState(drone, CMUDroneConfigurationUi.Key, state);
    }

    private List<CMUConfigurationOption> BuildFactionOptions(EntityUid user)
    {
        var factions = new List<CMUConfigurationOption>();
        var seen = new HashSet<string>();

        AddFactionOption(factions, seen, "AUWeYu");
        AddFactionOption(factions, seen, "GOVFOR");
        AddFactionOption(factions, seen, "OPFOR");
        AddFactionOption(factions, seen, "AUColonist");

        if (TryComp<NpcFactionMemberComponent>(user, out var member))
        {
            foreach (var faction in member.Factions)
                AddFactionOption(factions, seen, faction.Id);
        }

        return factions;
    }

    private void AddFactionOption(
        List<CMUConfigurationOption> factions,
        HashSet<string> seen,
        string id)
    {
        if (!seen.Add(id) || !_proto.TryIndex<NpcFactionPrototype>(id, out var faction))
            return;

        var name = string.IsNullOrEmpty(faction.Name)
            ? faction.ID
            : faction.Name;
        factions.Add(new CMUConfigurationOption(faction.ID, name));
    }

    private bool TryFindIffFaction(string npcFaction, out EntProtoId<IFFFactionComponent> faction)
    {
        if (SharedSentryTargetingSystem.SentryFactionToIff.TryGetValue(npcFaction, out faction))
            return true;

        if (TryGetIffPrototype(npcFaction, out faction))
            return true;

        if (TryGetIffPrototype($"Faction{npcFaction}", out faction))
            return true;

        faction = default;
        return false;
    }

    private bool TryGetIffPrototype(string id, out EntProtoId<IFFFactionComponent> faction)
    {
        faction = default;
        if (!_proto.TryIndex<EntityPrototype>(id, out var proto)
            || !proto.TryGetComponent<IFFFactionComponent>(out _, _factory))
            return false;

        faction = id;
        return true;
    }

    private HashSet<ProtoId<RadioChannelPrototype>> GetAvailableChannels(EntityUid user)
    {
        var channels = new HashSet<ProtoId<RadioChannelPrototype>>();

        if (TryComp<WearingHeadsetComponent>(user, out var wearing)
            && TryComp<EncryptionKeyHolderComponent>(wearing.Headset, out var keys))
            channels.UnionWith(keys.Channels);

        if (TryComp<IntrinsicRadioTransmitterComponent>(user, out var intrinsic))
            channels.UnionWith(intrinsic.Channels);

        if (TryComp<ActiveRadioComponent>(user, out var active))
            channels.UnionWith(active.Channels);

        return channels;
    }

    private bool TryFindHeldIdCard(EntityUid user, EntityUid key, out EntityUid idCard)
    {
        idCard = default;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held == key
                || !HasComp<IdCardComponent>(held)
                || !HasComp<AccessComponent>(held))
                continue;

            idCard = held;
            return true;
        }

        return false;
    }

    private bool IsHolding(EntityUid user, EntityUid item)
    {
        return _hands.IsHolding(user, item);
    }
}
