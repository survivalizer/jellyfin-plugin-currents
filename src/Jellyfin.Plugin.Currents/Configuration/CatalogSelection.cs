namespace Jellyfin.Plugin.Currents.Configuration;

/// <summary>An AIOMetadata catalog the admin chose to sync.</summary>
public class CatalogSelection
{
    /// <summary>Gets or sets the Stremio type used in catalog URLs, e.g. "movie", "series", "anime.series".</summary>
    public string Type { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public CatalogTarget Target { get; set; } = CatalogTarget.Movies;

    public int MaxItems { get; set; } = 100;

    public bool Enabled { get; set; } = true;

    /// <summary>Gets the stable key used in sync state ("{Type}/{Id}").</summary>
    public string Key => $"{Type}/{Id}";
}
