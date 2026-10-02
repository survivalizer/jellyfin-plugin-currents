using System.Collections.Concurrent;
using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Caches AIOMetadata metas so a library refresh does not refetch the same series per episode.</summary>
public sealed class MetaCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(1);
    private readonly TtlCache<string, bool> _misses;
    private readonly ConcurrentDictionary<string, Lazy<Task<StremioMeta?>>> _inFlight = new(StringComparer.Ordinal);
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
        _misses = new TtlCache<string, bool>(time);
    }

    public async Task<StremioMeta?> GetAsync(string type, string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"{type}/{id}";
        if (_cache.TryGet(key, out var hit))
        {
            return hit;
        }

        if (_misses.TryGet(key, out _)
            || !AioMetadataEndpoint.TryParse(_settings.Current.AioMetadataManifestUrl, out var endpoint, out _))
        {
            return null;
        }

        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<StremioMeta?>>(() => FetchAsync(key, endpoint, type, id)));
        var fetch = lazy.Value;
        _ = fetch.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<StremioMeta?>>>(key, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Shared by concurrent callers, so it never observes one caller's cancellation; the HttpClient timeout bounds it.
    private async Task<StremioMeta?> FetchAsync(string key, AioMetadataEndpoint endpoint, string type, string id)
    {
        try
        {
            var meta = await _client.GetMetaAsync(endpoint, type, id, CancellationToken.None).ConfigureAwait(false);
            if (meta is null)
            {
                _misses.Set(key, true, MissTtl);
                return null;
            }

            _cache.Set(key, meta, Ttl);
            return meta;
        }
        catch (Exception ex) when (ex is AioMetadataException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Could not load AIOMetadata meta for {Type} {Id}", type, id);
            _misses.Set(key, true, MissTtl);
            return null;
        }
        catch (OperationCanceledException ex)
        {
            // An HttpClient/per-attempt timeout (no caller token reaches this method): record a provider miss.
            _logger.LogWarning(ex, "Timed out loading AIOMetadata meta for {Type} {Id}", type, id);
            _misses.Set(key, true, MissTtl);
            return null;
        }
    }
}
