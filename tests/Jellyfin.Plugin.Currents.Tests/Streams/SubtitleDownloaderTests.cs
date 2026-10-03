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

    [Fact]
    public async Task A_body_that_drips_past_the_timeout_is_null()
    {
        var slow = new SubtitleDownloader(
            new FakeHttpClientFactory(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) })),
            TimeSpan.FromMilliseconds(100));

        Assert.Null(await slow.DownloadAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task A_connection_dropped_mid_body_is_null()
    {
        var dropped = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ThrowingStream()) });

        Assert.Null(await dropped.DownloadAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task A_corrupt_gzip_file_is_null()
    {
        var corrupt = Create(_ => Bytes([0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07, 0xFF, 0xFF]));

        Assert.Null(await corrupt.DownloadAsync(Url, CancellationToken.None));
    }

    private sealed class HangingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("connection reset");
    }
}
