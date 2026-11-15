using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.CMU14.ColonyEconomy;
using Content.Server.CMU14.Chemistry.Research;
using Content.Server.CMU14.Requisitions;
using Content.Server.CMU14.Robotics.QueueNetwork;
using Content.Server.GameTicking.Events;
using Content.Server._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions;
using Content.Shared._RMC14.Requisitions.Components;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CMU14.DroneOperator;
using Content.Shared.CMU14.Medical.Anatomy.BodyParts;
using Content.Shared.CMU14.Requisitions;
using Content.Shared.CMU14.Robotics;
using Content.Shared.CMU14.Robotics.QueueNetwork;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Paper;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Generates robotics work orders, queues synthetic transport crates in cargo,
/// and evaluates repair and build contracts when the corporate platform lowers.
/// </summary>
public sealed partial class CMURoboticsOrderConsoleSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private CorporateConsoleSystem _corporateConsole = default!;
    [Dependency] private CMURoboticsDamagedBodySystem _damagedBody = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private LabelSystem _labels = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private CMURoboticsOrderPoolSystem _pools = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CMUQueueNetworkConsoleSystem _queueNetwork = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private RequisitionsSystem _requisitions = default!;
    [Dependency] private ServerResearchDataTerminalSystem _research = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const int RepairRewardPerDamage = 1;
    private const int RepairRewardPerMissingLimb = 20;
    private const float RepairFailureFineMultiplier = 0.25f;
    private const float MissingElementPenaltyFraction = 0.05f;

    private static readonly EntProtoId TransportCrate = "CMUCrateSyntheticTransport";

    private static readonly BodyPartType[] ExternalLimbTypes =
    {
        BodyPartType.Arm,
        BodyPartType.Hand,
        BodyPartType.Leg,
        BodyPartType.Foot,
    };

    private int _nextOrderId = 1;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<CMURoboticsOrderConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CMURoboticsOrderConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CMURoboticsOrderConsoleComponent, CMURoboticsTerminalQueueUpdatedEvent>(OnQueueUpdated);
        SubscribeLocalEvent<RequisitionsElevatorComponent, CMURequisitionsElevatorLoweringAttemptEvent>(
            OnElevatorLoweringAttempt);
        SubscribeLocalEvent<CMURequisitionsCrateDeliveredEvent>(OnCrateDelivered);

        Subs.BuiEvents<CMURoboticsOrderConsoleComponent>(CMURoboticsOrderConsoleUiKey.Key, subs =>
        {
            subs.Event<CMURoboticsAcceptOrderMessage>(OnAcceptOrder);
            subs.Event<CMURoboticsCancelOrderMessage>(OnCancelOrder);
            subs.Event<CMURoboticsPrintSlipMessage>(OnPrintSlip);
            subs.Event<CMURoboticsRefreshOrdersMessage>(OnRefreshOrders);
            subs.Event<CMURoboticsPurchaseCartridgeMessage>(OnPurchaseCartridge);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<CMURoboticsOrderConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            var pool = _pools.GetPool(uid);
            if (pool.ActiveOrder is { } order)
            {
                if (order.Deadline != default && now >= order.Deadline)
                {
                    FailOrder((uid, console), pool, order, fine: true, reason: null);
                    continue;
                }
            }
            else if (now > pool.NextDroneOrderAllowed + TimeSpan.FromSeconds(1)
                     && now > pool.NextNetworkOrderAllowed + TimeSpan.FromSeconds(1))
            {
                continue;
            }

            if (now < console.NextUiUpdate
                || !_ui.IsUiOpen(uid, CMURoboticsOrderConsoleUiKey.Key))
                continue;

            console.NextUiUpdate = now + TimeSpan.FromSeconds(1);
            UpdateUi((uid, console));
        }
    }

    private void OnRoundStarting(RoundStartingEvent args)
    {
        _nextOrderId = 1;
        _nextUpdate = default;
        _pools.Reset();
    }

    private void OnMapInit(Entity<CMURoboticsOrderConsoleComponent> ent, ref MapInitEvent args)
    {
        FillOrders(ent);
    }

    private void OnUiOpened(Entity<CMURoboticsOrderConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        FillOrders(ent);
        ent.Comp.NextUiUpdate = _timing.CurTime;
        ent.Comp.LastQueueRevision = -1;
        UpdateUi(ent);
    }

    private void OnQueueUpdated(
        Entity<CMURoboticsOrderConsoleComponent> ent,
        ref CMURoboticsTerminalQueueUpdatedEvent args)
    {
        ent.Comp.NextUiUpdate = _timing.CurTime;
        UpdateUi(ent);
    }
}
