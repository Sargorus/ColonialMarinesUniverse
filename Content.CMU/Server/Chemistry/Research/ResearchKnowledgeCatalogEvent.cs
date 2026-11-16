using System.Collections.Generic;
using Content.Shared.CMU14.Chemistry.Research;
using Robust.Shared.GameObjects;

namespace Content.Server.CMU14.Chemistry.Research;

/// <summary>
/// Asks the research-document system to fill the chemistry terminal's knowledge catalog. The terminal
/// owns the UI state; the document system owns the fact registry, so the catalog travels by event.
/// </summary>
public sealed class ResearchKnowledgeCatalogEvent(string faction) : EntityEventArgs
{
    public readonly string Faction = faction;
    public readonly List<ResearchKnowledgeCatalogEntry> Entries = [];
}
