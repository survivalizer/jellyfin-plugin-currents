using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Caches AIOMetadata metas so a library refresh does not refetch the same series per episode.</summary>
public sealed class MetaCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly IAioMetadataClient _client;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<MetaCache> _logger;
    private readonly TtlCache<string, StremioMeta> _cache;

    public MetaCache(IAioMetadataClient client, ICurrentsSettings settings, TimeProvider time, ILogger<MetaCache> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
        _cache = new TtlCache<string, StremioMeta>(time);
    }

    public async Task<StremioMeta?> GetAsync(string type, string id, CancellationToken cancellationToken)
    {
        var key = $"{type}/{id}";
        if (_cache.TryGet(key, out var hit))
        {
            return hit;
        }

        if (!AioMetadataEndpoint.TryParse(_settings.Current.AioMetadataManifestUrl, out var endpoint, out _))
        {
            return null;
        }

        try
        {
            var meta = await _client.GetMetaAsync(endpoint, type, id, cancellationToken).ConfigureAwait(false);
            if (meta is not null)
            {
                _cache.Set(key, meta, Ttl);
            }

            return meta;
        }
        catch (Exception ex) when (ex is AioMetadataException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Could not load AIOMetadata meta for {Type} {Id}", type, id);
            return null;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // An HttpClient/per-attempt timeout, not a caller cancellation: record a provider miss.
            _logger.LogWarning(ex, "Timed out loading AIOMetadata meta for {Type} {Id}", type, id);
            return null;
        }
    }
}
