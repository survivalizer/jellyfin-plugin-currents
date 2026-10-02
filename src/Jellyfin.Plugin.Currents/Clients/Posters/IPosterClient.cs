namespace Jellyfin.Plugin.Currents.Clients.Posters;

/// <summary>Fetches posters for search cards on the server, so poster URLs (which may carry API keys) never reach clients.</summary>
public interface IPosterClient
{
    Task<PosterImage?> GetAsync(Uri uri, CancellationToken cancellationToken);
}
