using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Parasite;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server.CMU14.Chemistry.Research;

public sealed partial class ServerResearchDocumentSystem
{
    private void InitializeWitness()
    {
        SubscribeLocalEvent<ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<VictimInfectionChangedEvent>(OnInfectionChanged);
        SubscribeLocalEvent<VictimBurstComponent, ComponentStartup>(OnBurstStartup);
    }

    /// <summary>
    /// Examining a xenomorph, neomorph, facehugger or wendigo is the active "saw it" action. The species fact is
    /// created once per round and then copied into each valid observer's personal knowledge.
    /// </summary>
    private void OnExamined(ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !IsValidWitness(args.Examiner))
            return;
        if (!TryGetSightingFact(args.Examined, out var fact))
            return;
        if (!TryAddKnowledge(args.Examiner, fact.Id))
            return;
        PopupToUser(
            args.Examiner,
            Loc.GetString("cmu-research-documents-witness-learned", ("title", fact.Title)),
            args.Examiner);
        _adminLog.Add(
            LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Examiner)} observed {ToPrettyString(args.Examined)} and learned {fact.Id}");
    }

    private bool TryGetSightingFact(EntityUid examined, out ResearchFact fact)
    {
        fact = default!;
        if (HasComp<XenoHiddenComponent>(examined))
            return false;
        var meta = MetaData(examined);
        if (meta.EntityPrototype is not { } prototype)
            return false;
        var protoId = prototype.ID;
        var species = string.IsNullOrEmpty(prototype.Name) ? prototype.ID : Loc.GetString(prototype.Name);
        if (_tags.HasTag(examined, WendigoTag))
        {
            fact = RecordWitnessFact(
                $"wendigo:sighting:{protoId}",
                ResearchFactKind.WendigoSighting,
                Loc.GetString("cmu-research-fact-wendigo-sighting-title", ("name", species)),
                Loc.GetString("cmu-research-fact-wendigo-sighting-desc", ("name", species)),
                7,
                0.2f);
            return true;
        }
        if (!HasComp<XenoComponent>(examined) && !HasComp<XenoParasiteComponent>(examined))
            return false;
        fact = RecordWitnessFact(
            $"xeno:sighting:{protoId}",
            ResearchFactKind.XenoSighting,
            Loc.GetString("cmu-research-fact-xeno-sighting-title", ("name", species)),
            Loc.GetString("cmu-research-fact-xeno-sighting-desc", ("name", species)),
            5,
            0.15f);
        return true;
    }

    private void OnInfectionChanged(ref VictimInfectionChangedEvent args)
    {
        // The owner raises this event for both infection start and removal; only the start still has the
        // VictimInfectedComponent.
        if (!HasComp<VictimInfectedComponent>(args.Victim))
            return;
        NotifyObservers(
            args.Victim,
            "xeno:infection",
            ResearchFactKind.Infection,
            "cmu-research-fact-infection-title",
            "cmu-research-fact-infection-desc",
            5,
            0.15f);
    }

    private void OnBurstStartup(Entity<VictimBurstComponent> ent, ref ComponentStartup args)
    {
        NotifyObservers(
            ent.Owner,
            "xeno:burst",
            ResearchFactKind.Burst,
            "cmu-research-fact-burst-title",
            "cmu-research-fact-burst-desc",
            5,
            0.15f);
    }

    private void NotifyObservers(
        EntityUid source,
        string factId,
        ResearchFactKind kind,
        string titleKey,
        string descriptionKey,
        int sampleData,
        float intelPoints)
    {
        var coordinates = Transform(source).Coordinates;
        foreach (var player in _lookup.GetEntitiesInRange<MobStateComponent>(coordinates, 10f))
        {
            var uid = player.Owner;
            if (uid == source || !IsValidWitness(uid))
                continue;
            if (!_examine.InRangeUnOccluded(uid, source, 10f))
                continue;
            var fact = RecordWitnessFact(
                factId,
                kind,
                Loc.GetString(titleKey),
                Loc.GetString(descriptionKey),
                sampleData,
                intelPoints);
            if (!TryAddKnowledge(uid, fact.Id))
                continue;
            PopupToUser(
                uid,
                Loc.GetString("cmu-research-documents-witness-learned", ("title", fact.Title)),
                uid);
            _adminLog.Add(
                LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(uid)} witnessed {fact.Id} at {ToPrettyString(source)}");
        }
    }
}
