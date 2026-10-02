namespace Jellyfin.Plugin.Currents.Library;

/// <summary>What Currents remembers about a title it wrote.</summary>
public sealed class TitleState
{
    public string StateId { get; set; } = string.Empty;

    public MediaKind Kind { get; set; }

    public string StremioId { get; set; } = string.Empty;

    /// <summary>Gets or sets the folder relative to the library root, e.g. "Movies/Title (2024) [imdbid-tt1]".</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Gets or sets the catalog keys ("{type}/{id}") that have listed this title.</summary>
    public List<string> Catalogs { get; set; } = [];

    public bool AddedBySearch { get; set; }

    /// <summary>Gets or sets the number of consecutive successful syncs that did not list this title.</summary>
    public int MissCount { get; set; }

    public DateTimeOffset LastSeen { get; set; }
}
