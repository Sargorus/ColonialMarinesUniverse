using Content.IntegrationTests.Fixtures;
using Content.Server.Camera;
using Content.Server.SurveillanceCamera;
using Content.Shared.Camera;
using Content.Shared.SurveillanceCamera.Components;

namespace Content.IntegrationTests.CMU14.Surveillance;

[TestFixture]
public sealed class CMUCameraNetworkTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task ConstructedCameraCanJoinNetworkAndBeSeenByMonitor()
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

            var cameras = Server.System<SurveillanceCameraSystem>();
            Assert.That(cameras.TrySetNetwork((camera, cameraComp), "SurveillanceCameraSecurity"), Is.True,
                "a constructed camera must be assignable to one of its available networks");

            var networks = Server.System<CameraNetworkSystem>();
            Assert.That(networks.GetNetworkMembers("SurveillanceCameraSecurity"), Does.Contain(camera),
                "the camera must appear in the security network member list");
            Assert.That(networks.GetAccessibleCameras((monitor, monitorComp)), Does.Contain(camera),
                "the camera monitor must be able to see the camera once it joins the network");
        });
    }
}
