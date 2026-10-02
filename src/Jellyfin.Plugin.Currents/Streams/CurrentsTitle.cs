using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A Currents title as AIOStreams and AIOMetadata know it.</summary>
/// <param name="Type">The Stremio type (movie or series).</param>
/// <param name="StremioId">The Stremio id; for episodes it ends in :season:episode.</param>
public sealed record CurrentsTitle(string Type, string StremioId)
{
    /// <summary>Gets the series id for an episode id (drops ":S:E"); otherwise the id itself.</summary>
    public string SeriesId
    {
        get
        {
            if (Type != "series")
            {
                return StremioId;
            }

            var parts = StremioId.Split(':');
            foreach (var drop in new[] { 2, 1 })
            {
                if (parts.Length <= drop || !parts[^drop..].All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
                {
                    continue;
                }

                var rest = string.Join(':', parts[..^drop]);
                if (TitleKey.TryParse(MediaKind.Series, rest, out var key) && key.IsSeasonAware == (drop == 2))
                {
                    return rest;
                }
            }

            return StremioId;
        }
    }
}
