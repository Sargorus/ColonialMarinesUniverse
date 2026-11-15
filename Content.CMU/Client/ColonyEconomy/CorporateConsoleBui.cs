using Content.Shared.CMU14.ColonyEconomy;
using Robust.Client.UserInterface;

namespace Content.Client.CMU14.ColonyEconomy;

public sealed class CorporateConsoleBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CorporateConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CorporateConsoleWindow>();
        _window.OnWithdraw += amount => SendPredictedMessage(new CorporateConsoleWithdrawBuiMsg(amount));
        _window.OnSetTariff += percent => SendPredictedMessage(new CorporateConsoleSetTariffBuiMsg(percent));
        _window.OnOpenThirdParty += () => SendPredictedMessage(new CorporateConsoleOpenThirdPartyBuiMsg());
        _window.OnAcceptMission += id => SendPredictedMessage(new CorporateConsoleAcceptMissionBuiMsg(id));
        _window.OnSetDirection += direction => SendPredictedMessage(new CorporateConsoleSetDirectionBuiMsg(direction));
        _window.OnCallPmc += () => SendPredictedMessage(new CorporateConsoleCallPmcBuiMsg());
        _window.OnRequestCredit += () => SendPredictedMessage(new CorporateConsoleRequestCreditBuiMsg());
        _window.OnRequestAlienTech += () => SendPredictedMessage(new CorporateConsoleRequestAlienTechBuiMsg());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not CorporateConsoleBuiState s)
            return;

        _window.UpdateState(s);
    }
}
