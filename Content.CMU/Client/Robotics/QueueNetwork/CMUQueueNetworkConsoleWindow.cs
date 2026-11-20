using System.Numerics;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.Robotics.QueueNetwork;

/// <summary>
/// Standalone production diagnostic terminal. The robotics terminal embeds the
/// same <see cref="CMUQueueNetworkConsolePanel"/> as a tab.
/// </summary>
public sealed class CMUQueueNetworkConsoleWindow : DefaultWindow
{
    public readonly CMUQueueNetworkConsolePanel Panel;

    public CMUQueueNetworkConsoleWindow()
    {
        Title = Loc.GetString("cmu-queue-console-title");
        MinSize = new Vector2(720, 560);
        SetSize = new Vector2(900, 720);

        Panel = new CMUQueueNetworkConsolePanel();
        Contents.AddChild(Panel);
    }

    public void UpdateState(CMUQueueNetworkConsoleState state)
    {
        Panel.UpdateState(state);
    }
}
