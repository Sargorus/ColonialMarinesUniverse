using Content.Shared.CMU14.DroneOperator;
using Robust.Client.UserInterface;

namespace Content.Client.CMU14.DroneOperator;
public sealed class CMUDroneConfigurationBui : BoundUserInterface
{
    private CMUDroneConfigurationWindow? _window;

    public CMUDroneConfigurationBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CMUDroneConfigurationWindow>();
        _window.OnApply += (faction, channels, access) =>
            SendMessage(new CMUDroneConfigurationApplyMsg(faction, channels, access));
        _window.OnUtilityAction += message => SendMessage(message);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window != null && state is CMUDroneConfigurationBuiState configuration)
            _window.UpdateState(configuration);
    }
}
