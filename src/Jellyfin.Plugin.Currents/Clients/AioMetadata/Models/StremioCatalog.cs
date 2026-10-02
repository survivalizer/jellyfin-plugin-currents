using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>A catalog declared in a Stremio manifest.</summary>
public sealed class StremioCatalog
{
    public string Type { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public List<StremioExtra> Extra { get; set; } = [];

    /// <summary>Gets a value indicating whether the catalog needs an extra (e.g. search) and so cannot be synced.</summary>
    [JsonIgnore]
    public bool RequiresExtra => Extra.Exists(e => e.IsRequired);
}
