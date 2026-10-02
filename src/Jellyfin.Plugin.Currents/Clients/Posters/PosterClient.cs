using Jellyfin.Plugin.Currents.Clients.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Clients.Posters;

/// <summary>Fetches raster posters, bounded in size. Logs name the host only: RPDB poster URLs carry the user's key.</summary>
public sealed class PosterClient : IPosterClient
{
    internal const int MaxBytes = 10 * 1024 * 1024;

    // Served from Jellyfin's own origin, so no SVG (it can carry script) and nothing that is not a plain raster image.
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif" };
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<PosterClient> _logger;
    private readonly int _maxBytes;

    public PosterClient(IHttpClientFactory factory, ILogger<PosterClient> logger)
        : this(factory, logger, MaxBytes)
    {
    }

    internal PosterClient(IHttpClientFactory factory, ILogger<PosterClient> logger, int maxBytes)
    {
        _factory = factory;
        _logger = logger;
        _maxBytes = maxBytes;
    }

    public async Task<PosterImage?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        try
        {
            var client = _factory.CreateClient(HttpClientNames.Posters);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || type is null || !Allowed.Contains(type) || response.Content.Headers.ContentLength > _maxBytes)
            {
                _logger.LogInformation("Poster from {Host} was not usable ({Status}, {Type})", uri.Host, (int)response.StatusCode, type);
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > _maxBytes)
                    {
                        _logger.LogInformation("Poster from {Host} is larger than the limit", uri.Host);
                        return null;
                    }

                    await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                return new PosterImage(buffer.ToArray(), type);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogInformation("Could not fetch a poster from {Host}: {Error}", uri.Host, ex.GetType().Name);
            return null;
        }
    }
}
