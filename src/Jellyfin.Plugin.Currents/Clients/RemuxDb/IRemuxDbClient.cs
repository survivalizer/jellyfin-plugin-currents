namespace Jellyfin.Plugin.Currents.Clients.RemuxDb;

/// <summary>Reads probe data for a title from RemuxDB.</summary>
public interface IRemuxDbClient
{
    /// <summary>Gets every version RemuxDB knows for a title.</summary>
    /// <param name="externalId">"tt…" or "tmdb:…", plus ":{season}:{episode}" for an episode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The versions; empty when RemuxDB has none.</returns>
    Task<IReadOnlyList<RemuxDbVersion>> VersionsAsync(string externalId, CancellationToken cancellationToken);
}
