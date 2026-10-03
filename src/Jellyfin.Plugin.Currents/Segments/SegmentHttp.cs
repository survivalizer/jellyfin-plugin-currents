using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>The HTTP part every skip-marker source shares: send, turn failures into <see cref="SegmentSourceException"/>, read a bounded body.</summary>
internal static class SegmentHttp
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Sends a GET.</summary>
    /// <returns>The body, or null for 404.</returns>
    public static async Task<byte[]?> GetAsync(IHttpClientFactory factory, HttpRequestMessage request, string source, int maxBytes, Action<RetryConditionHeaderValue?>? onRateLimited, CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(HttpClientNames.Segments);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SegmentSourceException($"{source} is unreachable: {SecretMasker.Mask(ex.Message)}", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                onRateLimited?.Invoke(response.Headers.RetryAfter);
                throw new SegmentSourceException($"{source} answered 429 (too many requests).");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SegmentSourceException(string.Create(CultureInfo.InvariantCulture, $"{source} returned {(int)response.StatusCode}."));
            }

            try
            {
                return await BoundedContent.ReadAsync(response.Content, maxBytes, cancellationToken).ConfigureAwait(false)
                    ?? throw new SegmentSourceException($"{source} returned an oversized answer.");
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new SegmentSourceException($"{source} stopped answering: {SecretMasker.Mask(ex.Message)}", ex);
            }
        }
    }

    public static T Parse<T>(byte[] body, string source)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Options) ?? throw new SegmentSourceException($"{source} returned an empty answer.");
        }
        catch (JsonException ex)
        {
            throw new SegmentSourceException($"{source} returned invalid JSON.", ex);
        }
    }
}
