using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public class ProxyStreamResultTests
{
    private static readonly Uri Url = new("https://dav.example.com/real.mkv");
    private static readonly Dictionary<string, string> Headers = new() { ["Authorization"] = "Basic SECRET" };

    private static DefaultHttpContext Http(string? range = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Response.Body = new MemoryStream();
        if (range is not null)
        {
            http.Request.Headers.Range = range;
        }

        return http;
    }

    private static Task Run(StubHttpHandler stub, HttpContext http, ILogger? logger = null) =>
        new ProxyStreamResult(new FakeHttpClientFactory(stub), Url, Headers, logger ?? NullLogger.Instance).ExecuteResultAsync(new ActionContext { HttpContext = http });

    [Fact]
    public async Task Range_requests_are_relayed_with_206()
    {
        var stub = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1, 2, 3, 4]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(100, 103, 1000);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/x-matroska");
            response.Headers.AcceptRanges.Add("bytes");
            response.Headers.Add("Set-Cookie", "upstream=1");
            return response;
        });
        var http = Http("bytes=100-");

        await Run(stub, http);

        var sent = Assert.Single(stub.Sent);
        Assert.Equal("bytes=100-", sent.Headers["Range"]);
        Assert.Equal("Basic SECRET", sent.Headers["Authorization"]);
        Assert.Equal(206, http.Response.StatusCode);
        Assert.Equal("bytes 100-103/1000", http.Response.Headers.ContentRange.ToString());
        Assert.Equal("bytes", http.Response.Headers.AcceptRanges.ToString());
        Assert.Equal("video/x-matroska", http.Response.Headers.ContentType.ToString());
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ((MemoryStream)http.Response.Body).ToArray());
    }

    [Theory]
    [InlineData(HttpStatusCode.Found, 502)]
    [InlineData(HttpStatusCode.InternalServerError, 502)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable, 416)]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    public async Task Upstream_redirects_and_errors_are_relayed_without_a_body(HttpStatusCode upstream, int expected)
    {
        var http = Http();

        await Run(new StubHttpHandler(_ => new HttpResponseMessage(upstream) { Content = new StringContent("upstream body") }), http);

        Assert.Equal(expected, http.Response.StatusCode);
        Assert.Equal(0, http.Response.Body.Length);
    }

    [Fact]
    public async Task An_unreachable_upstream_is_a_bad_gateway()
    {
        var http = Http();

        await Run(new StubHttpHandler(_ => throw new HttpRequestException("refused")), http);

        Assert.Equal(502, http.Response.StatusCode);
    }

    [Fact]
    public async Task A_bad_gateway_is_logged_with_the_host_only()
    {
        var logger = new ListLogger<ProxyStreamResult>();

        await Run(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)), Http(), logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("https://dav.example.com", entry.Message, StringComparison.Ordinal);
        Assert.Contains("502", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("real.mkv", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_connection_failure_is_logged_with_the_host_and_exception_type_only()
    {
        var logger = new ListLogger<ProxyStreamResult>();

        await Run(new StubHttpHandler(_ => throw new HttpRequestException("refused https://dav.example.com/real.mkv Basic SECRET")), Http(), logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("https://dav.example.com", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(HttpRequestException), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("real.mkv", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", entry.Message, StringComparison.Ordinal);
    }
}
