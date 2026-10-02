namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>AIOStreams REST response envelope.</summary>
/// <typeparam name="T">Type of the data payload.</typeparam>
public sealed class ApiEnvelope<T>
    where T : class
{
    public bool Success { get; set; }

    public T? Data { get; set; }

    public ApiError? Error { get; set; }
}
