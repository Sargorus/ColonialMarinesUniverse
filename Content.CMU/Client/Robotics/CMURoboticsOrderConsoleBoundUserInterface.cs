using Content.Shared.CMU14.Robotics;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Client.UserInterface;

namespace Content.Client.CMU14.Robotics;

public sealed class CMURoboticsOrderConsoleBoundUserInterface : BoundUserInterface
{
    private CMURoboticsOrderConsoleWindow? _window;

    public CMURoboticsOrderConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CMURoboticsOrderConsoleWindow>();
        _window.OnAccept += id => SendMessage(new CMURoboticsAcceptOrderMessage(id));
        _window.OnCancel += () => SendMessage(new CMURoboticsCancelOrderMessage());
        _window.OnPrintSlip += () => SendMessage(new CMURoboticsPrintSlipMessage());
        _window.OnRefresh += () => SendMessage(new CMURoboticsRefreshOrdersMessage());
        _window.OnBuyCartridge += prototype =>
            SendMessage(new CMURoboticsPurchaseCartridgeMessage(prototype));

        var queue = _window.QueuePanel;
        queue.OnAnalyze += nodeId => SendMessage(new CMUQueueAnalyzeNodeMessage(nodeId));
        queue.OnSetServiceRate += (nodeId, rate) =>
            SendMessage(new CMUQueueSetServiceRateMessage(nodeId, rate));
        queue.OnReroute += bypassId => SendMessage(new CMUQueueRerouteMessage(bypassId));
        queue.OnReduceBypass += bypassId => SendMessage(new CMUQueueReduceBypassMessage(bypassId));
        queue.OnProject += () => SendMessage(new CMUQueueProjectMessage());
        queue.OnSubmit += () => SendMessage(new CMUQueueSubmitMessage());
        queue.OnNewContract += () => SendMessage(new CMUQueueNewContractMessage());
        queue.OnResetInterventions += () => SendMessage(new CMUQueueResetInterventionsMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CMURoboticsOrderConsoleState consoleState)
            _window?.UpdateState(consoleState);
    }
}
