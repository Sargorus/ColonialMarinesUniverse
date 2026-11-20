using Content.Server._RMC14.Rules;
using Content.Server.GameTicking;
using Content.Shared._RMC14.Marines.Squads;
using Content.Shared.CMU14.Squads;
using Robust.Shared.Prototypes;

namespace Content.Server.CMU14.Squads;

/// <summary>
/// CMU14: keeps the WY overwatch squads available on every game preset.
/// The distress signal rule spawns its own squads, but colony presets have no
/// squad-spawning rule. Without this the WeYa overwatch console has nothing to
/// show and the WY guard jobs' <see cref="AssignSquadComponent"/> finds no target.
/// </summary>
public sealed class CMUWeYaSquadSystem : EntitySystem
{
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SquadSystem _squad = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Run after the distress signal rule has spawned the squads it owns, so
        // TryEnsureSquad only fills in the ones that preset does not create.
        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnRulePlayerSpawning,
            after: [typeof(CMDistressSignalRuleSystem)]);
    }

    private void OnRulePlayerSpawning(RulePlayerSpawningEvent ev)
    {
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract
                || !prototype.HasComp<SquadTeamComponent>(_compFactory)
                || !prototype.HasComp<CMUSquadTitleSuffixComponent>(_compFactory))
                continue;

            _squad.TryEnsureSquad(prototype.ID, out _);
        }
    }
}
