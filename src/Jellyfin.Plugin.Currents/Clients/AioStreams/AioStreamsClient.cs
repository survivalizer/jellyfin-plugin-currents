using System.Net;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Clients.AioStreams;

/// <summary>HTTP implementation of <see cref="IAioStreamsClient"/>.</summary>
public sealed class AioStreamsClient : IAioStreamsClient
{
    /// <summary>The largest response body read from AIOStreams (16 MB).</summary>
    internal const int MaxBodyBytes = 16 * 1024 * 1024;
    private const string TooLarge = "AIOStreams returned a response larger than 16 MB.";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AioStreamsClient> _logger;
    private readonly int _maxBodyBytes;

    public AioStreamsClient(IHttpClientFactory httpClientFactory, ILogger<AioStreamsClient> logger)
        : this(httpClientFactory, logger, MaxBodyBytes)
    {
    }

    internal AioStreamsClient(IHttpClientFactory httpClientFactory, ILogger<AioStreamsClient> logger, int maxBodyBytes)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _maxBodyBytes = maxBodyBytes;
    }

    public async Task<SearchOutcome> SearchAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken)
    {
        var uri = credentials.Search(type, id);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = credentials.BasicAuth();

        var client = _httpClientFactory.CreateClient(HttpClientNames.AioStreams);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        ApiEnvelope<SearchData>? envelope = null;
        try
        {
            var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                envelope = JsonSerializer.Deserialize<ApiEnvelope<SearchData>>(body, JsonDefaults.Options);
            }
        }
        catch (JsonException ex)
        {
            throw new AioStreamsException($"AIOStreams returned invalid JSON ({(int)response.StatusCode}) for {SecretMasker.Mask(uri)}.", ex);
        }

        if (envelope?.Success == true && envelope.Data is { } data)
        {
            var errors = data.Errors.Select(e => SecretMasker.Mask(Describe(e))).ToList();
            if (errors.Count > 0)
            {
                _logger.LogInformation("AIOStreams reported {Count} addon errors for {Type} {Id}: {Errors}", errors.Count, type, id, errors);
            }

            return new SearchOutcome(data.Results, errors);
        }

        if (envelope?.Error?.Message is { } message)
        {
            throw new AioStreamsException($"AIOStreams: {SecretMasker.Mask(message)}");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AioStreamsException("AIOStreams rejected the credentials. Re-copy the manifest URL from AIOStreams.");
        }

        throw new AioStreamsException($"AIOStreams returned {(int)response.StatusCode} for {SecretMasker.Mask(uri)}.");
    }

    public async Task<IReadOnlyList<StremioSubtitle>> SubtitlesAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken)
    {
        var uri = credentials.Subtitles(type, id);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var client = _httpClientFactory.CreateClient(HttpClientNames.AioStreams);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AioStreamsException($"AIOStreams returned {(int)response.StatusCode} for {SecretMasker.Mask(uri)}.");
        }

        var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<SubtitlesResponse>(body, JsonDefaults.Options)?.Subtitles ?? [];
        }
        catch (JsonException ex)
        {
            throw new AioStreamsException($"AIOStreams returned invalid JSON for {SecretMasker.Mask(uri)}.", ex);
        }
    }

    private async Task<string> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _maxBodyBytes)
        {
            throw new AioStreamsException(TooLarge);
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > _maxBodyBytes)
                {
                    throw new AioStreamsException(TooLarge);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            var start = buffer.Length >= 3 && buffer.GetBuffer() is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
            return Encoding.UTF8.GetString(buffer.GetBuffer(), start, (int)buffer.Length - start);
        }
    }

    private static string Describe(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object)
        {
            return error.ToString();
        }

        var source = error.TryGetProperty("addon", out var addon) ? addon.ToString() : null;
        var text = error.TryGetProperty("description", out var d) ? d.ToString()
            : error.TryGetProperty("message", out var m) ? m.ToString()
            : error.TryGetProperty("title", out var t) ? t.ToString()
            : error.GetRawText();
        return source is null ? text : $"{source}: {text}";
    }
}
