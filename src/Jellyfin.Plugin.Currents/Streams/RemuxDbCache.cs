using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>RemuxDB versions per title: fetched once (single-flight) while a title's streams are searched, then matched to streams with no network call.</summary>
public sealed partial class RemuxDbCache
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ErrorTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _budget;
    private readonly IRemuxDbClient _client;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<RemuxDbCache> _logger;
    private readonly TtlCache<string, RemuxDbIndex> _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new(StringComparer.Ordinal);

    public RemuxDbCache(IRemuxDbClient client, ICurrentsSettings settings, TimeProvider time, ILogger<RemuxDbCache> logger)
        : this(client, settings, time, logger, LookupBudget)
    {
    }

    internal RemuxDbCache(IRemuxDbClient client, ICurrentsSettings settings, TimeProvider time, ILogger<RemuxDbCache> logger, TimeSpan budget)
    {
        _budget = budget;
        _client = client;
        _settings = settings;
        _logger = logger;
        _cache = new TtlCache<string, RemuxDbIndex>(time);
    }

    /// <summary>The RemuxDB id of a title: the IMDb or TMDB id, plus ":S:E" for an episode. Other ids (kitsu, absolute episode numbers) have none.</summary>
    /// <param name="title">The Currents title.</param>
    /// <returns>The id, or null.</returns>
    public static string? ExternalId(CurrentsTitle title)
    {
        var series = title.SeriesId;
        if (!TitleId().IsMatch(series))
        {
            return null;
        }

        if (title.Type != "series")
        {
            return series;
        }

        var suffix = title.StremioId.Length > series.Length ? title.StremioId[series.Length..] : string.Empty;
        return EpisodeSuffix().IsMatch(suffix) ? series + suffix : null;
    }

    public Task WarmAsync(CurrentsTitle title, CancellationToken cancellationToken)
    {
        if (!_settings.Current.EnableRemuxDb || ExternalId(title) is not { } id || _cache.TryGet(id, out _))
        {
            return Task.CompletedTask;
        }

        var lazy = _inFlight.GetOrAdd(id, key => new Lazy<Task>(() => FetchAsync(key)));
        var fetch = lazy.Value;
        _ = fetch.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task>>(id, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return fetch.WaitAsync(cancellationToken);
    }

    public RemuxDbVersion? Match(CurrentsTitle title, StreamResult result) =>
        _settings.Current.EnableRemuxDb && ExternalId(title) is { } id && _cache.TryGet(id, out var index) ? index.Match(result) : null;

    // Shared by concurrent callers, so it never observes a caller's token. The budget bounds the whole lookup, body read included
    // (HttpClient.Timeout covers only the response headers).
    private async Task FetchAsync(string id)
    {
        try
        {
            using var budget = new CancellationTokenSource(_budget);
            var versions = await _client.VersionsAsync(id, budget.Token).ConfigureAwait(false);
            _cache.Set(id, RemuxDbIndex.Create(versions), versions.Count > 0 ? HitTtl : MissTtl);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _cache.Set(id, RemuxDbIndex.Empty, ErrorTtl);
            _logger.LogInformation("RemuxDB lookup for {Id} failed; using the release name's tracks: {Reason}", id, SecretMasker.Mask(ex.Message));
        }
    }

    [GeneratedRegex("^(tt[0-9]+|tmdb:[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TitleId();

    [GeneratedRegex("^:[0-9]+:[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeSuffix();
}
