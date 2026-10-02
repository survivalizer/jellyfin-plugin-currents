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
    /// <summary>The largest response body read from AIOMetadata (16 MB).</summary>
    internal const int MaxBodyBytes = 16 * 1024 * 1024;
    private const string TooLarge = "AIOMetadata returned a response larger than 16 MB.";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AioMetadataClient> _logger;
    private readonly int _maxBodyBytes;

    public AioMetadataClient(IHttpClientFactory httpClientFactory, ILogger<AioMetadataClient> logger)
        : this(httpClientFactory, logger, MaxBodyBytes)
    {
    }

    internal AioMetadataClient(IHttpClientFactory httpClientFactory, ILogger<AioMetadataClient> logger, int maxBodyBytes)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _maxBodyBytes = maxBodyBytes;
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

    public async Task<IReadOnlyList<StremioMeta>> SearchAsync(AioMetadataEndpoint endpoint, string type, string catalogId, string query, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<CatalogResponse>(endpoint.Search(type, catalogId, query), cancellationToken).ConfigureAwait(false);
        return response?.Metas.Where(m => !string.IsNullOrWhiteSpace(m.Id)).ToList() ?? [];
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
            var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
            return body.Length == 0 ? null : JsonSerializer.Deserialize<T>(body.Span, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new AioMetadataException($"AIOMetadata returned invalid JSON for {SecretMasker.Mask(uri)}.", ex);
        }
    }

    // Bounded: a misbehaving server must not make Jellyfin buffer an unbounded body. A UTF-8 BOM is skipped.
    private async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _maxBodyBytes)
        {
            throw new AioMetadataException(TooLarge);
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
                    throw new AioMetadataException(TooLarge);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            var bytes = buffer.ToArray().AsMemory();
            return bytes.Span.StartsWith(Bom) ? bytes[Bom.Length..] : bytes;
        }
    }

    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];
}
