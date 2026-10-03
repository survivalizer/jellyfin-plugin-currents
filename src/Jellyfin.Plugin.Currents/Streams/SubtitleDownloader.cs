using System.IO.Compression;
using System.Text;
using Jellyfin.Plugin.Currents.Clients.Http;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Fetches one subtitle file and decodes it to text. Upstream URLs can embed keys, so nothing here is logged.</summary>
public sealed class SubtitleDownloader
{
    internal const int MaxBytes = 5 * 1024 * 1024;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly TimeSpan _timeout;

    public SubtitleDownloader(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, TimeSpan.FromSeconds(15))
    {
    }

    internal SubtitleDownloader(IHttpClientFactory httpClientFactory, TimeSpan timeout)
    {
        _httpClientFactory = httpClientFactory;
        _timeout = timeout;
    }

    /// <summary>Downloads the file.</summary>
    /// <param name="url">The upstream URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text, or null when the file is missing, larger than 5 MB, unreachable or not http(s).</returns>
    public async Task<string?> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        // The timeout covers the whole download, not just the response headers.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var client = _httpClientFactory.CreateClient(HttpClientNames.Subtitles);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await BoundedContent.ReadAsync(response.Content, MaxBytes, timeout.Token).ConfigureAwait(false);
            if (bytes is [0x1F, 0x8B, ..])
            {
                bytes = Gunzip(bytes);
            }

            return bytes is null ? null : Decode(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    // Old OpenSubtitles downloads are .gz files (not Content-Encoding), so gzip is detected by its magic bytes.
    private static byte[]? Gunzip(byte[] bytes)
    {
        using var packed = new MemoryStream(bytes);
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (output.Length + read > MaxBytes)
            {
                return null;
            }

            output.Write(chunk, 0, read);
        }

        return output.ToArray();
    }

    // UTF-8 (with or without a BOM), UTF-16 with a BOM, else Latin-1 (older SRT files).
    private static string Decode(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..])
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
