using System.Globalization;

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
            return parts.Length >= 3
                && int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    ? string.Join(':', parts[..^2])
                    : StremioId;
        }
    }
}
