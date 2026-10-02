using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Clients.AioStreams;

/// <summary>HTTP implementation of <see cref="IAioStreamsClient"/>.</summary>
public sealed class AioStreamsClient : IAioStreamsClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AioStreamsClient> _logger;

    public AioStreamsClient(IHttpClientFactory httpClientFactory, ILogger<AioStreamsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            var errors = data.Errors.Select(Describe).ToList();
            if (errors.Count > 0)
            {
                _logger.LogInformation("AIOStreams reported {Count} addon errors for {Type} {Id}: {Errors}", errors.Count, type, id, errors);
            }

            return new SearchOutcome(data.Results, errors);
        }

        if (envelope?.Error?.Message is { } message)
        {
            throw new AioStreamsException($"AIOStreams: {message}");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AioStreamsException("AIOStreams rejected the credentials. Re-copy the manifest URL from AIOStreams.");
        }

        throw new AioStreamsException($"AIOStreams returned {(int)response.StatusCode} for {SecretMasker.Mask(uri)}.");
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
