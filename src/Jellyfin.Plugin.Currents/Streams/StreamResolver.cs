using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Follows a stream's redirects, skips dead links and placeholder videos, and fails over to the next-ranked stream.</summary>
public sealed class StreamResolver : IStreamResolver
{
    private const int MaxRedirects = 5;
    private const string AutoKey = "auto";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(45);
    private readonly IStreamService _streams;
    private readonly StreamProfileResolver _profiles;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<StreamResolver> _logger;
    private readonly TtlCache<string, Uri> _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<ResolveResult>>> _inFlight = new(StringComparer.Ordinal);

    public StreamResolver(IStreamService streams, StreamProfileResolver profiles, IHttpClientFactory httpClientFactory, ICurrentsSettings settings, TimeProvider time, ILogger<StreamResolver> logger)
    {
        _streams = streams;
        _profiles = profiles;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _time = time;
        _logger = logger;
        _cache = new TtlCache<string, Uri>(time);
    }

    /// <inheritdoc />
    public Task<ResolveResult> ResolveAsync(string type, string stremioId, CancellationToken cancellationToken) =>
        ResolveAsync(_profiles.For(null), type, stremioId, null, cancellationToken);

    /// <inheritdoc />
    public Task<ResolveResult> ResolveAsync(VersionTicket ticket, CancellationToken cancellationToken) =>
        ResolveAsync(_profiles.For(ticket.UserId == Guid.Empty ? null : ticket.UserId), ticket.Type, ticket.StremioId, ticket.StreamKey, cancellationToken);

    private static string Origin(Uri uri) => $"{uri.Scheme}://{uri.Host}";

    private static string AddonName(StreamResult candidate) =>
        string.IsNullOrWhiteSpace(candidate.Addon) ? "unknown addon" : candidate.Addon;

    private async Task<ResolveResult> ResolveAsync(StreamProfile profile, string type, string stremioId, string? streamKey, CancellationToken cancellationToken)
    {
        if (!profile.CanPlay)
        {
            // The stream service owns the user-facing wording for disabled / unconfigured profiles.
            var reason = await _streams.GetAsync(profile, type, stremioId, SearchWait, cancellationToken).ConfigureAwait(false);
            return ResolveResult.Fail(reason.Error ?? "Streams are not available.");
        }

        var key = $"{profile.Credentials!.Fingerprint()}/{type}/{stremioId}/{streamKey ?? AutoKey}";
        if (_cache.TryGet(key, out var cached))
        {
            return new ResolveResult(cached, null);
        }

        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<ResolveResult>>(() => AttemptAsync(key, profile, type, stremioId, streamKey)));
        var attempt = lazy.Value;
        _ = attempt.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<ResolveResult>>>(key, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            return await attempt.WaitAsync(Deadline, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return ResolveResult.Fail("Timed out finding a playable stream.");
        }
    }

    // Shared by concurrent callers, so it never observes any single caller's cancellation; HttpClient timeouts bound it.
    private async Task<ResolveResult> AttemptAsync(string cacheKey, StreamProfile profile, string type, string stremioId, string? streamKey)
    {
        var lookup = await _streams.GetAsync(profile, type, stremioId, SearchWait, CancellationToken.None).ConfigureAwait(false);
        if (lookup.Error is not null || lookup.Streams.Count == 0)
        {
            return ResolveResult.Fail(lookup.Error ?? "No streams found for this title.");
        }

        var ordered = lookup.Streams.ToList();
        var chosen = streamKey is null ? -1 : ordered.FindIndex(s => s.Key == streamKey);
        if (chosen > 0)
        {
            var stream = ordered[chosen];
            ordered.RemoveAt(chosen);
            ordered.Insert(0, stream);
        }
        else if (streamKey is not null && chosen < 0)
        {
            _logger.LogInformation("The chosen version of {Id} is no longer offered; using the best available stream", stremioId);
        }

        var candidates = ordered.Take(Math.Max(1, _settings.Current.FailoverAttempts)).ToList();
        for (var index = 0; index < candidates.Count; index++)
        {
            // Candidate and final URLs carry debrid keys or tokens: only scheme://host, position and addon are logged.
            var candidate = candidates[index].Result;
            var requested = new Uri(candidate.Url!);
            var final = await FollowAsync(requested).ConfigureAwait(false);
            if (final is null)
            {
                _logger.LogInformation(
                    "Stream candidate {Index} ({Addon}, {Origin}) for {Id} is unreachable; trying the next one",
                    index + 1,
                    AddonName(candidate),
                    Origin(requested),
                    stremioId);
                continue;
            }

            if (PlaceholderDetector.IsPlaceholder(final, profile.Credentials!.BaseUri, requested))
            {
                _logger.LogInformation(
                    "Stream candidate {Index} ({Addon}, {Origin}) for {Id} returned a placeholder video; trying the next one",
                    index + 1,
                    AddonName(candidate),
                    Origin(requested),
                    stremioId);
                continue;
            }

            _cache.Set(cacheKey, final, CacheTtl);
            return new ResolveResult(final, null);
        }

        return ResolveResult.Fail("All streams failed or are not ready yet (still downloading?). Try again shortly.");
    }

    private async Task<Uri?> FollowAsync(Uri start)
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
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
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
