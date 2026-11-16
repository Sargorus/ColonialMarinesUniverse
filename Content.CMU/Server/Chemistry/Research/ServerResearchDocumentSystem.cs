using System;
using System.Collections.Generic;
using Content.Server.Administration.Logs;
using Content.Server.CMU14.Chemistry.Reagents;
using Content.Shared._RMC14.Chemistry.Reagent;
using Content.Shared._RMC14.Intel;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.CMU14.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry.Reagents;
using Content.Shared.CMU14.Chemistry.Research;
using Content.Shared.CMU14.Threats.Mobs.Wendigo;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Chemistry.Research;

/// <summary>
/// Research facts, personal knowledge and the paper documents that carry them. The scanner only creates facts;
/// a player must memorize or read a document, then submit it at a chemistry terminal or intel console.
/// </summary>
public sealed partial class ServerResearchDocumentSystem : EntitySystem
{
    public enum ResearchFactKind
    {
        Contract,
        Property,
        Botany,
        XenoSighting,
        WendigoSighting,
        Infection,
        Burst,
    }

    public sealed class ResearchFact
    {
        public string Id = string.Empty;
        public ResearchFactKind Kind;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public string? SourceFaction;
        public string? ReagentId;
        public int Credits;
        public int SampleData;
        public int Cash;
        public float IntelPoints;
        public int MaxSubmissions;
    }

    public readonly record struct ResearchSubmissionResult(
        int Accepted,
        int Skipped,
        int Credits,
        int SampleData,
        int Cash,
        float IntelPoints,
        string? SkippedReason = null);

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private ServerReagentGeneratorSystem _generator = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IntelSystem _intel = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private RMCReagentSystem _reagent = default!;
    [Dependency] private ServerResearchDataTerminalSystem _research = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private static readonly ProtoId<TagPrototype> WendigoTag = "Wendigo";
    private static readonly ProtoId<TagPrototype> WriteTag = "Write";

    /// <summary>How many different players may submit the same witness fact.</summary>
    public const int WitnessSubmissionLimit = 3;

    /// <summary>Sample data paid for one accepted witness fact.</summary>
    public const int WitnessSampleData = 3;

    private readonly Dictionary<string, ResearchFact> _facts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _witnessClaims = new(StringComparer.Ordinal);
    private readonly HashSet<(string FactId, EntityUid Player)> _playerClaims = [];
    private readonly Dictionary<string, HashSet<string>> _intelClaims = new(StringComparer.OrdinalIgnoreCase);

    public override void Initialize()
    {
        base.Initialize();
        InitializeKnowledge();
        InitializeCatalog();
        InitializeWriting();
        InitializeSubmission();
        InitializeIntel();
        InitializeWitness();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _facts.Clear();
        _witnessClaims.Clear();
        _playerClaims.Clear();
        _intelClaims.Clear();
    }

    /// <summary>Only scanner reports use the explicit "Remember" action; player documents use reading.</summary>
    public bool IsWitnessFact(ResearchFact fact)
        => fact.Kind is ResearchFactKind.XenoSighting
            or ResearchFactKind.WendigoSighting
            or ResearchFactKind.Infection
            or ResearchFactKind.Burst;

    public bool IsChemistryFact(ResearchFact fact)
        => fact.Kind is ResearchFactKind.Contract or ResearchFactKind.Property or ResearchFactKind.Botany;

    private bool IsValidWitness(EntityUid user)
    {
        if (HasComp<GhostComponent>(user)
            || HasComp<XenoComponent>(user)
            || HasComp<CMUWendigoJobComponent>(user))
        {
            return false;
        }
        return _mobState.IsAlive(user);
    }

    private bool TryAddKnowledge(EntityUid user, string factId)
    {
        if (!_facts.ContainsKey(factId))
            return false;
        var knowledge = EnsureComp<ResearchKnowledgeComponent>(user);
        return knowledge.Facts.Add(factId);
    }

    private int AddKnowledge(EntityUid user, IEnumerable<string> factIds)
    {
        var added = 0;
        foreach (var factId in factIds)
        {
            if (TryAddKnowledge(user, factId))
                added++;
        }
        return added;
    }

    private int CountNewFacts(EntityUid user, IEnumerable<string> factIds)
    {
        TryComp<ResearchKnowledgeComponent>(user, out var knowledge);
        var count = 0;
        foreach (var factId in factIds)
        {
            if (!_facts.ContainsKey(factId))
                continue;
            if (knowledge is not null && knowledge.Facts.Contains(factId))
                continue;
            count++;
        }
        return count;
    }

    private ResearchFact? GetFact(string factId)
        => _facts.GetValueOrDefault(factId);

    private void PopupToUser(EntityUid user, string message, EntityUid source)
    {
        if (!TryComp<ActorComponent>(user, out var actor) || actor.PlayerSession == null)
            return;
        _popup.PopupEntity(message, source, user);
    }

    public ResearchFact? GetFactForTest(string factId)
        => GetFact(factId);

    private string FormatReward(int credits, int sampleData, int cash)
    {
        if (credits == 0 && sampleData == 0 && cash == 0)
            return Loc.GetString("cmu-research-documents-reward-none");
        var parts = new List<string>();
        if (credits != 0)
            parts.Add(Loc.GetString("cmu-research-documents-reward-credits", ("credits", credits)));
        if (sampleData != 0)
            parts.Add(Loc.GetString("cmu-research-documents-reward-data", ("data", sampleData)));
        if (cash != 0)
            parts.Add(Loc.GetString("cmu-research-documents-reward-cash", ("cash", cash)));
        return string.Join(", ", parts);
    }

    private string DocumentTypeName(ResearchDocumentType type)
        => Loc.GetString($"cmu-research-documents-type-{type.ToString().ToLowerInvariant()}");
}
