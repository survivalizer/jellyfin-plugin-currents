namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>How the stream-list cache is doing since the server started (or since it was cleared).</summary>
/// <param name="Hits">Lookups answered from the cache.</param>
/// <param name="Misses">Lookups that searched AIOStreams (or joined a search in flight).</param>
/// <param name="Entries">Stream lists held now.</param>
public sealed record StreamCacheStats(long Hits, long Misses, int Entries);
