using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Clients.Posters;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class PosterClientTests
{
    private static readonly Uri Rpdb = new("https://api.ratingposterdb.com/t0-secretkey/imdb/poster-default/tt0133093.jpg");

    private static HttpResponseMessage Image(string type, int size = 3) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(new byte[size]) { Headers = { ContentType = new MediaTypeHeaderValue(type) } },
    };

    private static (PosterClient Client, FakeHttpClientFactory Factory, ListLogger<PosterClient> Logger) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, int maxBytes = PosterClient.MaxBytes, TimeSpan? timeout = null)
    {
        var factory = new FakeHttpClientFactory(new StubHttpHandler(respond));
        var logger = new ListLogger<PosterClient>();
        return (new PosterClient(factory, logger, maxBytes, timeout), factory, logger);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task Returns_raster_images(string type)
    {
        var (client, factory, _) = Create(_ => Image(type));

        var poster = await client.GetAsync(Rpdb, CancellationToken.None);

        Assert.Equal(type, poster!.ContentType);
        Assert.Equal(3, poster.Bytes.Length);
        Assert.Equal(HttpClientNames.Posters, Assert.Single(factory.RequestedNames));
        Assert.Equal(10 * 1024 * 1024, PosterClient.MaxBytes);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    public async Task Refuses_other_content_types(string type)
    {
        var (client, _, _) = Create(_ => Image(type));

        Assert.Null(await client.GetAsync(Rpdb, CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_oversized_bodies_failures_and_non_http_urls()
    {
        Assert.Null(await Create(_ => Image("image/jpeg", size: 20), maxBytes: 10).Client.GetAsync(Rpdb, CancellationToken.None));
        Assert.Null(await Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).Client.GetAsync(Rpdb, CancellationToken.None));
        Assert.Null(await Create(_ => throw new HttpRequestException("down")).Client.GetAsync(Rpdb, CancellationToken.None));
        Assert.Null(await Create(_ => Image("image/jpeg")).Client.GetAsync(new Uri("file:///etc/passwd"), CancellationToken.None));
    }

    [Fact]
    public async Task Logs_only_the_host()
    {
        var (client, _, logger) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await client.GetAsync(Rpdb, CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Message.Contains("api.ratingposterdb.com", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("secretkey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stalled_body_times_out_and_returns_null()
    {
        var (client, _, _) = Create(_ => Streamed(new ScriptedStream(stall: true)), timeout: TimeSpan.FromMilliseconds(100));

        var started = DateTime.UtcNow;
        Assert.Null(await client.GetAsync(Rpdb, CancellationToken.None));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_connection_reset_mid_body_returns_null()
    {
        var (client, _, _) = Create(_ => Streamed(new ScriptedStream(stall: false)));

        Assert.Null(await client.GetAsync(Rpdb, CancellationToken.None));
    }

    private static HttpResponseMessage Streamed(Stream body) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } },
    };

    private sealed class ScriptedStream(bool stall) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            throw new IOException("reset");
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
