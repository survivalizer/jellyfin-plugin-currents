using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Configuration;
using Jellyfin.Plugin.Currents.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Search;

/// <summary>Searches the configured AIOMetadata search catalogs. Upstream failures never surface; a failed catalog adds nothing.</summary>
public sealed partial class RemoteSearch
{
    private const int MinQueryLength = 2;
    private static readonly TimeSpan ResultTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(30);
    private readonly IAioMetadataClient _client;
    private readonly ICurrentsSettings _settings;
    private readonly SearchResultRegistry _registry;
    private readonly ILogger<RemoteSearch> _logger;
    private readonly TtlCache<string, IReadOnlyList<SearchResult>> _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SearchResult>>>> _inFlight = new(StringComparer.Ordinal);

    public RemoteSearch(IAioMetadataClient client, ICurrentsSettings settings, SearchResultRegistry registry, TimeProvider time, ILogger<RemoteSearch> logger)
    {
        _client = client;
        _settings = settings;
        _registry = registry;
        _logger = logger;
        _cache = new TtlCache<string, IReadOnlyList<SearchResult>>(time);
    }

    public static string Normalize(string? query) => Whitespace().Replace(query ?? string.Empty, " ").Trim();

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, IReadOnlyCollection<MediaKind> kinds, CancellationToken cancellationToken)
    {
        var config = _settings.Current;
        query = Normalize(query);
        if (query.Length < MinQueryLength || !config.EnableSearch
            || !AioMetadataEndpoint.TryParse(config.AioMetadataManifestUrl, out var endpoint, out _))
        {
            return [];
        }

        var lookups = config.SearchCatalogs
            .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.Type) && !string.IsNullOrWhiteSpace(c.Id) && kinds.Contains(KindOf(c)))
            .Select(c => CatalogAsync(endpoint, c, query))
            .ToList();
        var lists = await Task.WhenAll(lookups).WaitAsync(cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<SearchResult>();
        foreach (var result in lists.SelectMany(l => l))
        {
            if (seen.Add(result.Key.StateId))
            {
                merged.Add(result);
                _registry.Add(result);
            }
        }

        return merged;
    }

    private static MediaKind KindOf(CatalogSelection catalog) => catalog.Target == CatalogTarget.Movies ? MediaKind.Movie : MediaKind.Series;

    private Task<IReadOnlyList<SearchResult>> CatalogAsync(AioMetadataEndpoint endpoint, CatalogSelection catalog, string query)
    {
        var key = $"{catalog.Key}?{query.ToUpperInvariant()}";
        if (_cache.TryGet(key, out var cached))
        {
            return Task.FromResult(cached);
        }

        var lazy = _inFlight.GetOrAdd(key, k => new Lazy<Task<IReadOnlyList<SearchResult>>>(() => FetchAsync(k, endpoint, catalog, query)));
        var fetch = lazy.Value;
        _ = fetch.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<IReadOnlyList<SearchResult>>>>(key, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return fetch;
    }

    // Shared by concurrent callers, so it never observes one caller's cancellation; the HttpClient timeout bounds it.
    private async Task<IReadOnlyList<SearchResult>> FetchAsync(string cacheKey, AioMetadataEndpoint endpoint, CatalogSelection catalog, string query)
    {
        try
        {
            var metas = await _client.SearchAsync(endpoint, catalog.Type, catalog.Id, query, CancellationToken.None).ConfigureAwait(false);
            var kind = KindOf(catalog);
            IReadOnlyList<SearchResult> results = metas
                .Where(m => !m.Id.StartsWith("aiom.error.", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => TitleKey.FromMeta(kind, m) is { } key ? new SearchResult(key, m, catalog.Type) : null)
                .OfType<SearchResult>()
                .Take(Math.Clamp(catalog.MaxItems, 1, 100))
                .ToList();
            _cache.Set(cacheKey, results, ResultTtl);
            return results;
        }
        catch (Exception ex) when (ex is AioMetadataException or HttpRequestException or OperationCanceledException)
        {
            _logger.LogWarning("AIOMetadata search catalog {Catalog} failed: {Error}", catalog.Key, SecretMasker.Mask(ex.Message));
            _cache.Set(cacheKey, [], FailureTtl);
            return [];
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
