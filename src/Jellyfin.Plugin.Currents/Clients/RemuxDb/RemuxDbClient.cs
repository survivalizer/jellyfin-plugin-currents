using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Clients.RemuxDb;

/// <summary>HTTP implementation of <see cref="IRemuxDbClient"/>. Sends only the title id and a per-install client id.</summary>
public sealed partial class RemuxDbClient : IRemuxDbClient
{
    internal const int MaxBodyBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentsSettings _settings;

    public RemuxDbClient(IHttpClientFactory httpClientFactory, ICurrentsSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    /// <summary>A stable id for this install that reveals nothing about the signing secret. RemuxDB requires one.</summary>
    /// <param name="secret">The install <c>SigningSecret</c> (base64).</param>
    /// <returns>"currents-" and 32 hex characters.</returns>
    public static string ClientId(string secret) =>
        "currents-" + Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(secret), "currents/remuxdb-client-id"u8).AsSpan(0, 16));

    public async Task<IReadOnlyList<RemuxDbVersion>> VersionsAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!ExternalId().IsMatch(externalId))
        {
            throw new ArgumentException("Only IMDb and TMDB title ids are sent to RemuxDB.", nameof(externalId));
        }

        if (!Uri.TryCreate(_settings.Current.RemuxDbUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new RemuxDbException("The RemuxDB URL is not an http(s) URL.");
        }

        // The id is validated above, so it needs no escaping (and RemuxDB expects the colons as they are).
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, $"api/media/{externalId}/versions"));
        request.Headers.Add("x-client-id", ClientId(_settings.Current.SigningSecret));
        var client = _httpClientFactory.CreateClient(HttpClientNames.RemuxDb);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new RemuxDbException($"RemuxDB returned {(int)response.StatusCode}.");
        }

        var body = await BoundedContent.ReadAsync(response.Content, MaxBodyBytes, cancellationToken).ConfigureAwait(false)
            ?? throw new RemuxDbException("RemuxDB returned a response larger than 8 MB.");
        try
        {
            return JsonSerializer.Deserialize<List<RemuxDbVersion>>(body, Options) ?? [];
        }
        catch (JsonException ex)
        {
            throw new RemuxDbException("RemuxDB returned invalid JSON.", ex);
        }
    }

    [GeneratedRegex("^(tt[0-9]+|tmdb:[0-9]+)(:[0-9]+:[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ExternalId();
}
