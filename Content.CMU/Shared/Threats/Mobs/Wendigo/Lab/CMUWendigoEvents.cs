using Robust.Shared.GameObjects;

namespace Content.Shared.CMU14.Threats.Mobs.Wendigo.Lab;

/// <summary>
/// Raised after the lab procedure turns a subject into a Wendigo. Admin-spawned and round-threat Wendigos
/// never go through this path, so company missions only count laboratory creations.
/// </summary>
public sealed class CMUWendigoCreatedEvent(EntityUid wendigo, bool fullWendigo, bool tamed) : EntityEventArgs
{
    public readonly EntityUid Wendigo = wendigo;

    /// <summary>True for the cannibal-grown full Wendigo; false for the lesser variant.</summary>
    public readonly bool FullWendigo = fullWendigo;

    /// <summary>True when the result carries <c>CMUWendigoTamedComponent</c>.</summary>
    public readonly bool Tamed = tamed;
}
