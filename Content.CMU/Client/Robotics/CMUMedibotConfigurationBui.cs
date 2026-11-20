using Content.Shared.CMU14.Robotics;
using Robust.Client.UserInterface;

namespace Content.Client.CMU14.Robotics;

public sealed class CMUMedibotConfigurationBui : BoundUserInterface
{
    private CMUMedibotConfigurationWindow? _window;

    public CMUMedibotConfigurationBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CMUMedibotConfigurationWindow>();
        _window.OnApply += (amount, brute, burn, critical, factions) =>
            SendMessage(new CMUMedibotConfigurationApplyMsg(amount, brute, burn, critical, factions));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window != null && state is CMUMedibotConfigurationBuiState configuration)
            _window.UpdateState(configuration);
    }
}
