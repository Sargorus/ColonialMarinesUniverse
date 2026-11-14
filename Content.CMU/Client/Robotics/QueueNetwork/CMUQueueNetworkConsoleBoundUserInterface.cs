using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Client.UserInterface;

namespace Content.Client.CMU14.Robotics.QueueNetwork;

public sealed class CMUQueueNetworkConsoleBoundUserInterface : BoundUserInterface
{
    private CMUQueueNetworkConsoleWindow? _window;

    public CMUQueueNetworkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CMUQueueNetworkConsoleWindow>();
        _window.OnAnalyze += nodeId => SendMessage(new CMUQueueAnalyzeNodeMessage(nodeId));
        _window.OnUpgrade += nodeId => SendMessage(new CMUQueueUpgradeNodeMessage(nodeId));
        _window.OnProject += () => SendMessage(new CMUQueueProjectMessage());
        _window.OnSubmit += () => SendMessage(new CMUQueueSubmitMessage());
        _window.OnNewContract += () => SendMessage(new CMUQueueNewContractMessage());
        _window.OnResetUpgrades += () => SendMessage(new CMUQueueResetUpgradesMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CMUQueueNetworkConsoleState consoleState)
            _window?.UpdateState(consoleState);
    }
}
