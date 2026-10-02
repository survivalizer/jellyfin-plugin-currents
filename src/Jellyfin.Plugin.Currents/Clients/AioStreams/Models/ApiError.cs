namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>Error part of the AIOStreams envelope.</summary>
public sealed class ApiError
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}
