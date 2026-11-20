using Content.Shared.CMU14.Robotics.UtilityDrones;
using Robust.Client.UserInterface;

namespace Content.Client.CMU14.Robotics.UtilityDrones;

public sealed class CMUUtilityDroneBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CMUUtilityDroneConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CMUUtilityDroneConsoleWindow>();
        _window.Command += SendMessage;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CMUUtilityDroneConsoleState console)
            _window?.UpdateState(console);
    }
}
