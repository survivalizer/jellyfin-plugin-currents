namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>A catalog "extra" parameter (skip, genre, search…).</summary>
public sealed class StremioExtra
{
    public string Name { get; set; } = string.Empty;

    public bool IsRequired { get; set; }
}
