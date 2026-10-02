using System.Net.Http.Headers;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Degraded-mode resolver: default AIOStreams config, AIOStreams order, failover past dead links and placeholders.</summary>
public sealed class StreamResolver : IStreamResolver
{
    private const int MaxRedirects = 5;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly IAioStreamsClient _streams;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<StreamResolver> _logger;
    private readonly TtlCache<string, Uri> _cache;

    /// <summary>Initializes a new instance of the <see cref="StreamResolver"/> class.</summary>
    /// <param name="streams">The AIOStreams client.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="time">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public StreamResolver(IAioStreamsClient streams, IHttpClientFactory httpClientFactory, ICurrentsSettings settings, TimeProvider time, ILogger<StreamResolver> logger)
    {
        _streams = streams;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _cache = new TtlCache<string, Uri>(time);
    }

    /// <inheritdoc />
    public async Task<ResolveResult> ResolveAsync(string type, string stremioId, CancellationToken cancellationToken)
    {
        var cacheKey = $"{type}/{stremioId}";
        if (_cache.TryGet(cacheKey, out var cached))
        {
            return new ResolveResult(cached, null);
        }

        var config = _settings.Current;
        if (!AioStreamsCredentials.TryParse(config.AioStreamsManifestUrl, out var credentials, out _))
        {
            return ResolveResult.Fail("AIOStreams is not configured. Set the default AIOStreams manifest URL in the Currents settings.");
        }

        SearchOutcome outcome;
        try
        {
            outcome = await _streams.SearchAsync(credentials, type, stremioId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AioStreamsException or HttpRequestException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "AIOStreams search failed for {Type} {Id}", type, stremioId);
            return ResolveResult.Fail($"AIOStreams search failed: {SecretMasker.Mask(ex.Message)}");
        }

        var candidates = StreamRanker.Rank(outcome.Results)
            .Where(r => r.RequestHeaders is null || r.RequestHeaders.Count == 0)
            .Take(Math.Max(1, config.FailoverAttempts))
            .ToList();

        foreach (var candidate in candidates)
        {
            var requested = new Uri(candidate.Url!);
            var final = await FollowAsync(requested, cancellationToken).ConfigureAwait(false);
            if (final is null)
            {
                _logger.LogInformation("Stream {Url} for {Id} is unreachable; trying the next one", SecretMasker.Mask(candidate.Url), stremioId);
                continue;
            }

            if (PlaceholderDetector.IsPlaceholder(final, credentials.BaseUri, requested))
            {
                _logger.LogInformation("Stream {Url} for {Id} returned a placeholder video; trying the next one", SecretMasker.Mask(candidate.Url), stremioId);
                continue;
            }

            _cache.Set(cacheKey, final, CacheTtl);
            return new ResolveResult(final, null);
        }

        return ResolveResult.Fail(outcome.Results.Count == 0
            ? "No streams found for this title."
            : "All streams failed or are not ready yet (still downloading?). Try again shortly.");
    }

    private async Task<Uri?> FollowAsync(Uri start, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.Resolve);
        var current = start;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (current.Scheme != Uri.UriSchemeHttp && current.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return null;
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                return response.IsSuccessStatusCode ? current : null;
            }
        }

        return null;
    }
}
