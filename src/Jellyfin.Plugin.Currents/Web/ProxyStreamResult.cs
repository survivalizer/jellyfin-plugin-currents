using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Streams;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>
/// Streams a header-bound file to Jellyfin's ffmpeg: the stream's headers are added server-side, Range is relayed so seeking works,
/// and only content headers come back. Upstream redirects are not followed (credentials stay on the resolved origin).
/// </summary>
public sealed class ProxyStreamResult : IActionResult
{
    private static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] Relayed = ["Content-Type", "Content-Length", "Content-Range", "Accept-Ranges", "Last-Modified", "ETag"];
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri _url;
    private readonly IReadOnlyDictionary<string, string> _headers;

    public ProxyStreamResult(IHttpClientFactory httpClientFactory, Uri url, IReadOnlyDictionary<string, string> headers)
    {
        _httpClientFactory = httpClientFactory;
        _url = url;
        _headers = headers;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var aborted = http.RequestAborted;
        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        StreamHeaders.Apply(request, _headers);
        foreach (var name in new[] { "Range", "If-Range" })
        {
            if (http.Request.Headers.TryGetValue(name, out var value) && value.Count > 0)
            {
                request.Headers.TryAddWithoutValidation(name, value.ToString());
            }
        }

        HttpResponseMessage response;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted))
        {
            timeout.CancelAfter(HeadersTimeout);
            try
            {
                response = await _httpClientFactory.CreateClient(HttpClientNames.Proxy)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !aborted.IsCancellationRequested))
            {
                http.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 or >= 500)
            {
                http.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            http.Response.StatusCode = status;
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            foreach (var name in Relayed)
            {
                if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                {
                    http.Response.Headers[name] = values.ToArray();
                }
            }

            var body = await response.Content.ReadAsStreamAsync(aborted).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                try
                {
                    await body.CopyToAsync(http.Response.Body, aborted).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
                {
                    // Mid-body failure: abort, so ffmpeg sees a broken transfer rather than a clean (truncated) end.
                    http.Abort();
                }
            }
        }
    }
}
