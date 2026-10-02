using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Clients.AioMetadata;

/// <summary>HTTP implementation of <see cref="IAioMetadataClient"/>.</summary>
public sealed class AioMetadataClient : IAioMetadataClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AioMetadataClient> _logger;

    public AioMetadataClient(IHttpClientFactory httpClientFactory, ILogger<AioMetadataClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<StremioManifest> GetManifestAsync(AioMetadataEndpoint endpoint, CancellationToken cancellationToken) =>
        await GetJsonAsync<StremioManifest>(endpoint.Manifest, cancellationToken).ConfigureAwait(false)
        ?? throw new AioMetadataException("AIOMetadata returned an empty manifest.");

    public async Task<IReadOnlyList<StremioMeta>> GetCatalogPageAsync(AioMetadataEndpoint endpoint, string type, string catalogId, int skip, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<CatalogResponse>(endpoint.Catalog(type, catalogId, skip), cancellationToken).ConfigureAwait(false);
        return response?.Metas.Where(m => !string.IsNullOrWhiteSpace(m.Id)).ToList() ?? [];
    }

    public async Task<StremioMeta?> GetMetaAsync(AioMetadataEndpoint endpoint, string type, string id, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<MetaResponse>(endpoint.Meta(type, id), cancellationToken).ConfigureAwait(false);
        return response?.Meta;
    }

    private async Task<T?> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
        where T : class
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.AioMetadata);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("AIOMetadata 404 for {Url}", SecretMasker.Mask(uri));
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AioMetadataException($"AIOMetadata returned {(int)response.StatusCode} for {SecretMasker.Mask(uri)}.");
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            throw new AioMetadataException($"AIOMetadata returned invalid JSON for {SecretMasker.Mask(uri)}.", ex);
        }
    }
}
