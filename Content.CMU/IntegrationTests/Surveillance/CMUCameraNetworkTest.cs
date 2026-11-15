using Content.IntegrationTests.Fixtures;
using Content.Server.Camera;
using Content.Server.SurveillanceCamera;
using Content.Shared.Camera;
using Content.Shared.SurveillanceCamera.Components;
using System.Linq;

namespace Content.IntegrationTests.CMU14.Surveillance;

[TestFixture]
public sealed class CMUCameraNetworkTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task ConstructedCameraCanJoinColonyNetworkAndBeSeenByMonitor()
    {
        var map = await Pair.CreateTestMap();
        EntityUid camera = default;
        EntityUid monitor = default;
        SurveillanceCameraComponent cameraComp = default!;
        CameraNetworkReceiverComponent monitorComp = default!;

        await Server.WaitAssertion(() =>
        {
            camera = SEntMan.SpawnEntity("SurveillanceCameraConstructed", map.GridCoords);
            monitor = SEntMan.SpawnEntity("ComputerSurveillanceCameraMonitor", map.GridCoords);
            cameraComp = SEntMan.GetComponent<SurveillanceCameraComponent>(camera);
            monitorComp = SEntMan.GetComponent<CameraNetworkReceiverComponent>(monitor);

            Assert.That(cameraComp.AvailableNetworks.Select(id => id.Id), Is.EquivalentTo(new[]
            {
                "CMUSurveillanceCameraColonyCMB",
                "CMUSurveillanceCameraColonyGOVFOR",
                "CMUSurveillanceCameraColonyWEYU",
            }), "the constructed camera must only offer the colony networks");

            var cameras = Server.System<SurveillanceCameraSystem>();
            Assert.That(cameras.TrySetNetwork((camera, cameraComp), "CMUSurveillanceCameraColonyCMB"), Is.True,
                "a constructed camera must be assignable to a colony network");

            var networks = Server.System<CameraNetworkSystem>();
            Assert.That(networks.GetNetworkMembers("CMUSurveillanceCameraColonyCMB"), Does.Contain(camera),
                "the camera must appear in the CMB network member list");
            Assert.That(networks.GetAccessibleCameras((monitor, monitorComp)), Does.Contain(camera),
                "the camera monitor must be able to see the camera once it joins the network");
        });
    }

    [Test]
    public async Task MonitorSeesRuntimeCameraNetwork()
    {
        var map = await Pair.CreateTestMap();
        EntityUid camera = default;
        EntityUid monitor = default;
        CameraNetworkReceiverComponent monitorComp = default!;
        EntityUid network = default;

        await Server.WaitAssertion(() =>
        {
            camera = SEntMan.SpawnEntity("SurveillanceCameraConstructed", map.GridCoords);
            monitor = SEntMan.SpawnEntity("ComputerSurveillanceCameraMonitor", map.GridCoords);
            monitorComp = SEntMan.GetComponent<CameraNetworkReceiverComponent>(monitor);

            var networks = Server.System<CameraNetworkSystem>();
            network = networks.CreateNetwork("Colony maintenance", camera);
            Assert.That(networks.SetMemberNetworkEntities(camera, [network]), Is.True,
                "the camera must join the runtime network");

            Assert.That(networks.GetEffectiveNetworkEntities(monitor), Does.Contain(network),
                "the monitor must receive every runtime camera network");
            Assert.That(networks.GetAccessibleCameras((monitor, monitorComp)), Does.Contain(camera),
                "the monitor must see cameras on runtime networks");
        });
    }
}
