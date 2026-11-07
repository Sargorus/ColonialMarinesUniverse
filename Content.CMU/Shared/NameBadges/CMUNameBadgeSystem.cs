using Content.Shared.NameModifier.EntitySystems;

namespace Content.Shared.CMU14.NameBadges;

/// <summary>
/// CMU14: adds the stored wearer name to the badge's entity name.
/// </summary>
public sealed class CMUNameBadgeSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUNameBadgeComponent, RefreshNameModifiersEvent>(OnRefreshNameModifiers);
    }

    private void OnRefreshNameModifiers(Entity<CMUNameBadgeComponent> ent, ref RefreshNameModifiersEvent args)
    {
        if (!string.IsNullOrWhiteSpace(ent.Comp.Name))
            args.AddModifier("cmu-name-badge", extraArgs: ("name", ent.Comp.Name));
    }
}
