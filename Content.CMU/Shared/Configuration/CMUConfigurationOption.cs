using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Configuration;

/// <summary>
/// Generic id/name pair used by CMU configuration UIs. The name is a
/// localization id or a raw prototype id; the client resolves it when drawing.
/// </summary>
[Serializable, NetSerializable]
public sealed class CMUConfigurationOption
{
    public string Id { get; }
    public string Name { get; }

    public CMUConfigurationOption(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
