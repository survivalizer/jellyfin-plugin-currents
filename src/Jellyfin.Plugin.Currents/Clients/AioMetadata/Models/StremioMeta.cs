using System.Text.Json.Serialization;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>A Stremio meta object as served by AIOMetadata. Fields starting with "_" are ignored.</summary>
public sealed class StremioMeta
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    public string? Type { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Year { get; set; }

    public string? ReleaseInfo { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Released { get; set; }

    public string? Poster { get; set; }

    public string? Background { get; set; }

    public string? Logo { get; set; }

    public List<string>? Genres { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Runtime { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ImdbRating { get; set; }

    public List<StremioVideo>? Videos { get; set; }

    public List<StremioTrailer>? Trailers { get; set; }
}
