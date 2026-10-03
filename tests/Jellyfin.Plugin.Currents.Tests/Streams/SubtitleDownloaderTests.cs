using System.IO.Compression;
using System.Net;
using System.Text;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class SubtitleDownloaderTests
{
    private static readonly Uri Url = new("https://subs.example.com/file/1");

    private static SubtitleDownloader Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new FakeHttpClientFactory(new StubHttpHandler(respond)));

    private static HttpResponseMessage Bytes(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    [Fact]
    public async Task Gzipped_latin1_files_are_decoded()
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.Latin1.GetBytes("Café"));
        }

        Assert.Equal("Café", await Create(_ => Bytes(packed.ToArray())).DownloadAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task Utf8_and_utf16_with_a_bom_are_decoded()
    {
        var utf16 = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Åse")).ToArray();
        var utf8 = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Åse")).ToArray();

        Assert.Equal("Åse", await Create(_ => Bytes(utf16)).DownloadAsync(Url, CancellationToken.None));
        Assert.Equal("Åse", await Create(_ => Bytes(utf8)).DownloadAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task Large_missing_unreachable_and_non_http_files_are_null()
    {
        var large = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[SubtitleDownloader.MaxBytes + 1])) });
        var missing = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var unreachable = Create(_ => throw new HttpRequestException("connection refused"));

        Assert.Null(await large.DownloadAsync(Url, CancellationToken.None));
        Assert.Null(await missing.DownloadAsync(Url, CancellationToken.None));
        Assert.Null(await unreachable.DownloadAsync(Url, CancellationToken.None));
        Assert.Null(await missing.DownloadAsync(new Uri("file:///etc/passwd"), CancellationToken.None));
    }
}
