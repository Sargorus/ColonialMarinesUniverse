using Content.Server.Atmos.EntitySystems;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos.Visuals;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Atmos;

/// <summary>
/// Handles <see cref="SignalGasVentComponent"/>: accepts a gas tank, releases its
/// contents into the environment while open and updates the vent visuals.
/// </summary>
public sealed partial class SignalGasVentSystem : EntitySystem
{
    private static readonly TimeSpan TankDirtyInterval = TimeSpan.FromSeconds(5);

    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DeviceLinkSystem _signal = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SignalGasVentComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SignalGasVentComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<SignalGasVentComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
        SubscribeLocalEvent<SignalGasVentComponent, GasAnalyzerScanEvent>(OnAnalyzed);
        SubscribeLocalEvent<SignalGasVentComponent, EntInsertedIntoContainerMessage>(OnTankInserted);
    }

    private void OnInit(Entity<SignalGasVentComponent> ent, ref ComponentInit args)
    {
        _signal.EnsureSinkPorts(ent, ent.Comp.OpenPort, ent.Comp.ClosePort, ent.Comp.TogglePort);
        UpdateVisuals(ent);
    }

    private void OnSignalReceived(Entity<SignalGasVentComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port == ent.Comp.OpenPort)
            SetEnabled(ent, true);
        else if (args.Port == ent.Comp.ClosePort)
            SetEnabled(ent, false);
        else if (args.Port == ent.Comp.TogglePort)
            SetEnabled(ent, !ent.Comp.Enabled);
    }

    private void OnTankInserted(Entity<SignalGasVentComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.SlotId)
            return;

        // A tank that was left with its own release valve open would leak
        // through the vent regardless of signals, so close it on insertion.
        if (TryComp<GasTankComponent>(args.Entity, out var gasTank))
        {
            gasTank.ReleaseValveOpen = false;
            Dirty(args.Entity, gasTank);
        }

        UpdateVisuals(ent);
    }

    private void OnAtmosUpdate(Entity<SignalGasVentComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (GetTank(ent) is not { } tank)
        {
            SetEnabled(ent, false);
            return;
        }

        var environment = _atmosphere.GetContainingMixture(ent.Owner, false, true);
        var deltaP = environment == null
            ? tank.Comp.Air.Pressure
            : tank.Comp.Air.Pressure - environment.Pressure;

        deltaP = Math.Min(deltaP, ent.Comp.ReleasePressure);

        var removed = _atmosphere.FlowGas(tank.Comp.Air, deltaP, args.dt, ent.Comp.ReleaseArea);

        if (environment != null && removed != null)
            _atmosphere.Merge(environment, removed);

        if (removed == null)
            return;

        if (_timing.CurTime >= ent.Comp.NextReleaseSound)
        {
            ent.Comp.NextReleaseSound = _timing.CurTime + ent.Comp.ReleaseSoundInterval;
            _audio.PlayPvs(ent.Comp.ReleaseSound, ent);
        }

        if (removed.TotalMoles <= 0)
            return;

        if (_timing.CurTime >= ent.Comp.NextTankDirty)
        {
            ent.Comp.NextTankDirty = _timing.CurTime + TankDirtyInterval;
            Dirty(tank.Owner, tank.Comp);
        }
    }

    private void OnAnalyzed(Entity<SignalGasVentComponent> ent, ref GasAnalyzerScanEvent args)
    {
        if (GetTank(ent) is not { } tank)
            return;

        args.GasMixtures ??= new List<(string, GasMixture?)>();
        args.GasMixtures.Add((Name(tank.Owner), tank.Comp.Air));
    }

    private void SetEnabled(Entity<SignalGasVentComponent> ent, bool enabled)
    {
        if (ent.Comp.Enabled == enabled)
            return;

        if (enabled && GetTank(ent) == null)
            return;

        ent.Comp.Enabled = enabled;
        ent.Comp.NextReleaseSound = TimeSpan.Zero;
        UpdateVisuals(ent);
    }

    private void UpdateVisuals(Entity<SignalGasVentComponent> ent)
    {
        var state = ent.Comp.Enabled && GetTank(ent) != null
            ? VentPumpState.Out
            : VentPumpState.Off;

        _appearance.SetData(ent, VentPumpVisuals.State, state);
    }

    private Entity<GasTankComponent>? GetTank(Entity<SignalGasVentComponent> ent)
    {
        var item = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.SlotId);
        if (item == null)
            return null;

        return TryComp<GasTankComponent>(item, out var tank)
            ? (item.Value, tank)
            : null;
    }
}
