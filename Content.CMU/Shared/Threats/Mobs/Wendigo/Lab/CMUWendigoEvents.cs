using Robust.Shared.GameObjects;

namespace Content.Shared.CMU14.Threats.Mobs.Wendigo.Lab;

/// <summary>
/// Raised after the lab procedure turns a subject into a Wendigo. Admin-spawned and round-threat Wendigos
/// never go through this path, so company missions only count laboratory creations.
/// </summary>
[ByRefEvent]
public readonly record struct CMUWendigoCreatedEvent(EntityUid Wendigo, bool FullWendigo, bool Tamed);
