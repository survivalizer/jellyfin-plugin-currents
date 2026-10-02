namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Turns a title id into a playable stream URL.</summary>
public interface IStreamResolver
{
    /// <summary>Resolves a playable URL for a title.</summary>
    /// <param name="type">The Stremio type (movie or series).</param>
    /// <param name="stremioId">The Stremio id (e.g. tt1234567 or tt1234567:1:2).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolve result.</returns>
    Task<ResolveResult> ResolveAsync(string type, string stremioId, CancellationToken cancellationToken);
}
