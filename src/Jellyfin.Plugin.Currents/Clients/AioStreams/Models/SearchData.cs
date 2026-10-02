using System.Text.Json;

namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>Payload of GET /api/v1/search.</summary>
public sealed class SearchData
{
    public List<StreamResult> Results { get; set; } = [];

    public List<JsonElement> Errors { get; set; } = [];
}
