using System;
using System.Collections.Generic;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CMU14.Medical.Anatomy.BodyParts.Events;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Applies randomized missing limbs and burn/brute damage used by repair-order
/// androids. Runs after the initial body has been assembled.
/// </summary>
public sealed partial class CMURoboticsDamagedBodySystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CMURoboticsDamagedBodyComponent, MapInitEvent>(
            OnMapInit,
            after: new[] { typeof(InitialBodySystem) });
    }

    private void OnMapInit(Entity<CMURoboticsDamagedBodyComponent> ent, ref MapInitEvent args)
    {
        if (!ent.Comp.RandomizeOnMapInit)
            return;

        if (!TryComp<BodyComponent>(ent.Owner, out var body))
            return;

        var missing = RollRange(ent.Comp.MinSeveredLimbs, ent.Comp.MaxSeveredLimbs);
        var burn = RollRange(ent.Comp.MinBurnDamage, ent.Comp.MaxBurnDamage);
        var brute = RollRange(ent.Comp.MinBruteDamage, ent.Comp.MaxBruteDamage);

        ApplyRepairProfile((ent.Owner, ent.Comp), body, missing, burn, brute);
    }

    /// <summary>
    /// Applies a pre-rolled repair profile to an order unit. The console rolls
    /// the damage when it creates the order, so the reward shown on the order
    /// is final before the player accepts it.
    /// </summary>
    public bool ApplyRepairProfile(EntityUid uid, int missingLimbs, int burn, int brute)
    {
        if (!TryComp<CMURoboticsDamagedBodyComponent>(uid, out var damaged)
            || !TryComp<BodyComponent>(uid, out var body))
        {
            return false;
        }

        ApplyRepairProfile((uid, damaged), body, missingLimbs, burn, brute);
        return true;
    }

    private void ApplyRepairProfile(
        Entity<CMURoboticsDamagedBodyComponent> ent,
        BodyComponent body,
        int missingLimbs,
        int burn,
        int brute)
    {
        SeverLimbs(ent, body, missingLimbs);
        ApplyDamage(ent, burn, brute);
    }

    private void ApplyDamage(Entity<CMURoboticsDamagedBodyComponent> ent, int burn, int brute)
    {
        if (burn > 0)
        {
            var heat = _prototypes.Index(ent.Comp.BurnDamageType);
            _damageable.TryChangeDamage(
                ent.Owner,
                new DamageSpecifier(heat, FixedPoint2.New(burn)),
                ignoreResistances: true);
        }

        if (brute > 0)
        {
            var blunt = _prototypes.Index(ent.Comp.BruteDamageType);
            _damageable.TryChangeDamage(
                ent.Owner,
                new DamageSpecifier(blunt, FixedPoint2.New(brute)),
                ignoreResistances: true);
        }
    }

    private void SeverLimbs(
        Entity<CMURoboticsDamagedBodyComponent> ent,
        BodyComponent body,
        int count)
    {
        var limbs = new List<(EntityUid Part, BodyPartType Type, BodyPartSymmetry Symmetry)>();
        foreach (var type in ent.Comp.SeverableParts)
        {
            foreach (var part in _body.GetBodyChildrenOfType(ent.Owner, type, body))
                limbs.Add((part.Id, type, part.Component.Symmetry));
        }

        if (limbs.Count == 0)
            return;

        _random.Shuffle(limbs);
        count = Math.Clamp(count, 0, limbs.Count);

        for (var i = 0; i < count; i++)
        {
            var limb = limbs[i];
            var ev = new BodyPartSeverAttemptEvent(ent.Owner, limb.Part, limb.Type, Surgical: true);
            RaiseLocalEvent(limb.Part, ref ev);

            if (ev.Succeeded
                && ev.DetachedBody is { } detached
                && !TerminatingOrDeleted(detached))
                QueueDel(detached);
        }
    }

    private int RollRange(int min, int max)
    {
        var low = Math.Min(min, max);
        var high = Math.Max(min, max);
        return _random.Next(low, high + 1);
    }
}
