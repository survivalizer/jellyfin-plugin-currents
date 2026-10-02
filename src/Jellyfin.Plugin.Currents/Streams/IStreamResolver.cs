namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Turns a title (or one version of it) into a playable stream URL.</summary>
public interface IStreamResolver
{
    /// <summary>Resolves the best stream for a title with the server default config (degraded .strm playback).</summary>
    /// <param name="type">The Stremio type (movie or series).</param>
    /// <param name="stremioId">The Stremio id (e.g. tt1234567 or tt1234567:1:2).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolve result.</returns>
    Task<ResolveResult> ResolveAsync(string type, string stremioId, CancellationToken cancellationToken);

    /// <summary>Resolves one version for the ticket's user: that stream first, then the user's next-ranked streams.</summary>
    /// <param name="ticket">The verified version ticket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolve result.</returns>
    Task<ResolveResult> ResolveAsync(VersionTicket ticket, CancellationToken cancellationToken);
}
