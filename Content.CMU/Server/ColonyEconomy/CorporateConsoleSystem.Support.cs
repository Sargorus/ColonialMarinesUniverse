using System;
using System.Collections.Generic;
using Content.Server.CMU14.Ops.ThirdParty;
using Content.Server._RMC14.Requisitions;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.CMU14.Threats;
using Content.Shared.Database;
using Content.Shared._RMC14.Requisitions;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleSystem
{
    private const string PmcPartyId = "CMUWYPMCTeamSmall";
    private const string GuardPartyId = "CMUWYSecurityTeam";
    private static readonly EntProtoId YautjaTechnologyCrate = "CMUCrateSecureYautjaTechnology";
    private static readonly ProtoId<ThirdPartyPrototype> PmcParty = new(PmcPartyId);
    private static readonly ProtoId<ThirdPartyPrototype> GuardParty = new(GuardPartyId);

    [Dependency] private RequisitionsSystem _requisitions = default!;

    private void OnCallPmc(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleCallPmcBuiMsg msg)
    {
        if (comp.PmcCalls >= comp.MaxPmcCalls || _timing.CurTime < comp.NextPmcCallAt)
            return;

        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (state.CompanyValue < comp.PmcCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-pmc-no-value");
            UpdateAllUi();
            return;
        }

        if (!TrySpawnSupportParty(PmcParty, msg.Actor, uid))
            return;

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor)} called a WEYU PMC team at {ToPrettyString(uid)}");

        var nextCall = _timing.CurTime + comp.PmcCooldown;
        var query = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var other))
        {
            other.PmcCalls++;
            other.NextPmcCallAt = nextCall;
            SetSupportStatus(other, "cmu-corporate-pmc-called");
            _ui.SetUiState(consoleUid, CorporateConsoleUi.Key, BuildUiState(consoleUid, other));
        }
    }

    private void OnCallGuard(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleCallGuardBuiMsg msg)
    {
        if (comp.GuardCalls >= comp.MaxGuardCalls || _timing.CurTime < comp.NextGuardCallAt)
            return;

        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (state.CompanyValue < comp.GuardCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-guard-no-value");
            UpdateAllUi();
            return;
        }

        if (!TrySpawnSupportParty(GuardParty, msg.Actor, uid))
            return;

        state.CompanyValue -= comp.GuardCostCompanyValue;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor)} called a WEYU guard squad at {ToPrettyString(uid)} " +
            $"for {comp.GuardCostCompanyValue} company value ({state.CompanyValue} left)");

        var nextCall = _timing.CurTime + comp.GuardCooldown;
        var query = EntityQueryEnumerator<CorporateConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var other))
        {
            other.GuardCalls++;
            other.NextGuardCallAt = nextCall;
            SetSupportStatus(other, "cmu-corporate-guard-called");
            _ui.SetUiState(consoleUid, CorporateConsoleUi.Key, BuildUiState(consoleUid, other));
        }
    }

    private void OnRequestCredit(EntityUid uid, CorporateConsoleComponent comp, CorporateConsoleRequestCreditBuiMsg msg)
    {
        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (_timing.CurTime < comp.NextCreditAt)
            return;

        if (state.CompanyValue < comp.CreditCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-credit-no-value");
            UpdateAllUi();
            return;
        }

        state.CompanyValue -= comp.CreditCostCompanyValue;
        comp.NextCreditAt = _timing.CurTime + comp.CreditCooldown;
        AddToCorporateBudget(comp.CreditAmount);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor)} requested {comp.CreditAmount} credits at " +
            $"{ToPrettyString(uid)} for {comp.CreditCostCompanyValue} company value");
        SetSupportStatus(comp, "cmu-corporate-credit-granted");
        UpdateAllUi();
    }

    private void OnRequestAlienTech(EntityUid uid, CorporateConsoleComponent comp,
        CorporateConsoleRequestAlienTechBuiMsg msg)
    {
        var faction = GetConsoleFaction(uid);
        var state = GetMissionState(faction);
        if (state.CompanyValue < comp.AlienTechCostCompanyValue)
        {
            SetSupportStatus(comp, "cmu-corporate-alien-tech-no-value");
            UpdateAllUi();
            return;
        }

        if (FindCorporateElevator(uid) is not { } elevator)
        {
            SetSupportStatus(comp, "cmu-corporate-alien-tech-no-cargo");
            UpdateAllUi();
            return;
        }

        var entry = new RequisitionsEntry
        {
            Crate = YautjaTechnologyCrate,
        };

        if (!_requisitions.TryAddElevatorOrders(elevator, new List<RequisitionsEntry> { entry }))
        {
            SetSupportStatus(comp, "cmu-corporate-alien-tech-queue-full");
            UpdateAllUi();
            return;
        }

        state.CompanyValue -= comp.AlienTechCostCompanyValue;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor)} ordered alien technology at {ToPrettyString(uid)} " +
            $"for {comp.AlienTechCostCompanyValue} company value");
        SetSupportStatus(comp, "cmu-corporate-alien-tech-queued");
        UpdateAllUi();
    }

    private bool TrySpawnSupportParty(
        ProtoId<ThirdPartyPrototype> partyId,
        EntityUid actor,
        EntityUid console)
    {
        if (!actor.IsValid() || TerminatingOrDeleted(actor))
            return false;

        if (!_proto.TryIndex(partyId, out var partyProto)
            || !_auRound.IsThirdPartyAllowedForCurrentContext(partyProto)
            || !_proto.TryIndex(partyProto.PartySpawn, out var spawnProto))
        {
            return false;
        }

        _thirdParty.SpawnThirdParty(partyProto, spawnProto, false,
            caller: new ThirdPartyCaller(
                Name(actor),
                Loc.GetString("cmu-third-party-caller-corporate"),
                Actor: actor,
                Console: console));
        return true;
    }

    private void SetSupportStatus(CorporateConsoleComponent comp, string locId)
    {
        comp.SupportStatus = Loc.GetString(locId);
    }
}
