using System.Collections.Concurrent;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Caches AIOStreams searches per (config, title), coalesces concurrent searches, and ranks per caller.</summary>
public sealed class StreamService : IStreamService
{
    private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(30);
    private readonly IAioStreamsClient _client;
    private readonly ICurrentsSettings _settings;
    private readonly DiagnosticsLog _diagnostics;
    private readonly TimeProvider _time;
    private readonly ILogger<StreamService> _logger;
    private readonly TtlCache<string, SearchOutcome> _results;
    private readonly TtlCache<string, string> _failures;
    private readonly ConcurrentDictionary<string, Lazy<Task<SearchOutcome>>> _inFlight = new(StringComparer.Ordinal);
    private long _hits;
    private long _misses;

    public StreamService(IAioStreamsClient client, ICurrentsSettings settings, DiagnosticsLog diagnostics, TimeProvider time, ILogger<StreamService> logger)
    {
        _client = client;
        _settings = settings;
        _diagnostics = diagnostics;
        _time = time;
        _logger = logger;
        _results = new TtlCache<string, SearchOutcome>(time);
        _failures = new TtlCache<string, string>(time);
    }

    public async Task<StreamLookup> GetAsync(StreamProfile profile, string type, string stremioId, TimeSpan wait, CancellationToken cancellationToken)
    {
        if (Unavailable(profile) is { } reason)
        {
            return reason;
        }

        var key = CacheKey(profile.Credentials!, type, stremioId);
        if (_results.TryGet(key, out var cached))
        {
            Interlocked.Increment(ref _hits);
            return Rank(cached, profile.Preferences);
        }

        Interlocked.Increment(ref _misses);
        if (_failures.TryGet(key, out var error))
        {
            return StreamLookup.Fail(error);
        }

        var lazy = _inFlight.GetOrAdd(key, k => new Lazy<Task<SearchOutcome>>(() => SearchAsync(k, profile.Credentials!, type, stremioId)));
        var search = lazy.Value;

        // Runs inline once the search completes (immediately if it already has), so a finished search never lingers as "in flight".
        _ = search.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<SearchOutcome>>>(key, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            return Rank(await search.WaitAsync(wait, _time, cancellationToken).ConfigureAwait(false), profile.Preferences);
        }
        catch (TimeoutException)
        {
            return StreamLookup.Fail("Streams are taking a while to load. Reopen this title in a moment.");
        }
        catch (Exception ex) when (IsUpstreamFailure(ex, cancellationToken))
        {
            return StreamLookup.Fail(_failures.TryGet(key, out var message) ? message : Describe(ex));
        }
    }

    public StreamLookup? Peek(StreamProfile profile, string type, string stremioId)
    {
        if (Unavailable(profile) is { } reason)
        {
            return reason;
        }

        return _results.TryGet(CacheKey(profile.Credentials!, type, stremioId), out var cached)
            ? Rank(cached, profile.Preferences)
            : null;
    }

    public void Clear()
    {
        _results.Clear();
        _failures.Clear();
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    public StreamCacheStats Stats() => new(Interlocked.Read(ref _hits), Interlocked.Read(ref _misses), _results.Count);

    private static StreamLookup? Unavailable(StreamProfile profile)
    {
        if (profile.Disabled)
        {
            return StreamLookup.Fail("Streams are disabled for your account.");
        }

        return profile.Credentials is null
            ? StreamLookup.Fail("Streams are not configured. Ask your admin, or add your AIOStreams config on the Currents user page.")
            : null;
    }

    private static StreamLookup Rank(SearchOutcome outcome, StreamPreferences preferences)
    {
        if (outcome.Results.Count == 0)
        {
            return StreamLookup.Fail("No streams found for this title.");
        }

        var keys = StreamIdentity.Keys(outcome.Results);
        var keyOf = new Dictionary<StreamResult, string>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < keys.Count; i++)
        {
            keyOf[outcome.Results[i]] = keys[i];
        }

        var ranked = StreamRanker.Rank(outcome.Results, preferences).Select(r => new RankedStream(keyOf[r], r)).ToList();
        return ranked.Count == 0
            ? StreamLookup.Fail("No streams match your stream preferences.")
            : new StreamLookup(ranked, null);
    }

    private static string CacheKey(AioStreamsCredentials credentials, string type, string stremioId) =>
        $"{credentials.Fingerprint()}/{type}/{stremioId}";

    private static bool IsUpstreamFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is AioStreamsException or HttpRequestException
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static string Describe(Exception ex) => $"AIOStreams search failed: {SecretMasker.Mask(ex.Message)}";

    private async Task<SearchOutcome> SearchAsync(string key, AioStreamsCredentials credentials, string type, string stremioId)
    {
        try
        {
            // Not tied to any caller: others may be waiting on it, and a late result still fills the cache.
            var outcome = await _client.SearchAsync(credentials, type, stremioId, CancellationToken.None).ConfigureAwait(false);

            // Empty because addons failed is probably transient: retry soon rather than hiding the title for the full TTL.
            var ttl = outcome.Results.Count == 0 && outcome.Errors.Count > 0
                ? FailureTtl
                : TimeSpan.FromMinutes(Math.Max(1, _settings.Current.StreamCacheMinutes));
            _results.Set(key, outcome, ttl);
            return outcome;
        }
        catch (Exception ex) when (IsUpstreamFailure(ex, CancellationToken.None))
        {
            _logger.LogWarning("AIOStreams search failed for {Type} {Id}: {Reason}", type, stremioId, SecretMasker.Mask(ex.Message));
            _failures.Set(key, Describe(ex), FailureTtl);
            _diagnostics.Record("Streams", $"Search for {type} {stremioId} failed: {ex.Message}");
            throw;
        }
    }
}
