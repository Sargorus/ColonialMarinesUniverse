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
        _window.Panel.OnAnalyze += nodeId => SendMessage(new CMUQueueAnalyzeNodeMessage(nodeId));
        _window.Panel.OnSetServiceRate += (nodeId, rate) => SendMessage(new CMUQueueSetServiceRateMessage(nodeId, rate));
        _window.Panel.OnReroute += bypassId => SendMessage(new CMUQueueRerouteMessage(bypassId));
        _window.Panel.OnReduceBypass += bypassId => SendMessage(new CMUQueueReduceBypassMessage(bypassId));
        _window.Panel.OnProject += () => SendMessage(new CMUQueueProjectMessage());
        _window.Panel.OnSubmit += () => SendMessage(new CMUQueueSubmitMessage());
        _window.Panel.OnNewContract += () => SendMessage(new CMUQueueNewContractMessage());
        _window.Panel.OnResetInterventions += () => SendMessage(new CMUQueueResetInterventionsMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CMUQueueNetworkConsoleState consoleState)
            _window?.UpdateState(consoleState);
    }
}
