using System.Text.Json.Serialization;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>An episode entry in a series meta's "videos" array.</summary>
public sealed class StremioVideo
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? Name { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Released { get; set; }

    public string? Overview { get; set; }

    public string? Description { get; set; }

    public string? Thumbnail { get; set; }
}
