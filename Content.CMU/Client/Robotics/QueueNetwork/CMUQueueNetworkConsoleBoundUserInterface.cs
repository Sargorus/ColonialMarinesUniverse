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
        _window.OnSetServiceRate += (nodeId, rate) => SendMessage(new CMUQueueSetServiceRateMessage(nodeId, rate));
        _window.OnReroute += bypassId => SendMessage(new CMUQueueRerouteMessage(bypassId));
        _window.OnProject += () => SendMessage(new CMUQueueProjectMessage());
        _window.OnSubmit += () => SendMessage(new CMUQueueSubmitMessage());
        _window.OnNewContract += () => SendMessage(new CMUQueueNewContractMessage());
        _window.OnResetInterventions += () => SendMessage(new CMUQueueResetInterventionsMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CMUQueueNetworkConsoleState consoleState)
            _window?.UpdateState(consoleState);
    }
}
