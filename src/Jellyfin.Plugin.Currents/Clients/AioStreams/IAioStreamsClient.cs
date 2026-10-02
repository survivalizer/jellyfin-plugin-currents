namespace Jellyfin.Plugin.Currents.Clients.AioStreams;

/// <summary>Finds streams for a title through the AIOStreams search API.</summary>
public interface IAioStreamsClient
{
    Task<SearchOutcome> SearchAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken);
}
