namespace Jellyfin.Plugin.Currents.Web;

/// <summary>One cache's numbers since the server started (or the cache was cleared).</summary>
/// <param name="Hits">Lookups answered from the cache.</param>
/// <param name="Misses">Lookups that went upstream.</param>
/// <param name="Entries">Entries held now.</param>
/// <param name="HitRate">Hits / (hits + misses), or null before the first lookup.</param>
public sealed record CacheInfo(long Hits, long Misses, int Entries, double? HitRate);
