using Jellyfin.Plugin.Currents.Clients.Posters;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class PosterCacheTests
{
    private static readonly Uri Poster = new("https://img.example.com/a.jpg");
    private readonly FakePosterClient _posters = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);

    private PosterCache Create(int maxEntries = 200, long maxBytes = 64L * 1024 * 1024) => new(_posters, _time, maxEntries, maxBytes);

    [Fact]
    public async Task A_second_request_for_the_same_id_is_served_from_memory()
    {
        var cache = Create();
        var id = Guid.NewGuid();

        var first = await cache.GetAsync(id, Poster, CancellationToken.None);
        var second = await cache.GetAsync(id, Poster, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, first!.Bytes);
        Assert.Same(first, second);
        Assert.Single(_posters.Requests);
    }

    [Fact]
    public async Task Concurrent_requests_for_one_id_make_one_upstream_fetch()
    {
        var cache = Create();
        var id = Guid.NewGuid();
        _posters.Gate = new TaskCompletionSource();

        var requests = Enumerable.Range(0, 5).Select(_ => cache.GetAsync(id, Poster, CancellationToken.None)).ToList();
        _posters.Gate.SetResult();
        var images = await Task.WhenAll(requests);

        Assert.All(images, i => Assert.Equal("image/jpeg", i!.ContentType));
        Assert.Single(_posters.Requests);
    }

    [Fact]
    public async Task A_cancelled_caller_does_not_cancel_the_shared_fetch()
    {
        var cache = Create();
        var id = Guid.NewGuid();
        _posters.Gate = new TaskCompletionSource();
        using var cancel = new CancellationTokenSource();

        var cancelled = cache.GetAsync(id, Poster, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var waiting = cache.GetAsync(id, Poster, CancellationToken.None);
        _posters.Gate.SetResult();

        Assert.NotNull(await waiting);
        Assert.Single(_posters.Requests);
    }

    [Fact]
    public async Task A_missing_poster_is_remembered_for_five_minutes()
    {
        var cache = Create();
        var id = Guid.NewGuid();
        _posters.Image = null;

        Assert.Null(await cache.GetAsync(id, Poster, CancellationToken.None));
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Null(await cache.GetAsync(id, Poster, CancellationToken.None));
        Assert.Single(_posters.Requests);

        _time.Advance(TimeSpan.FromMinutes(2));
        _posters.Image = new PosterImage([9], "image/png");
        Assert.Equal(new byte[] { 9 }, (await cache.GetAsync(id, Poster, CancellationToken.None))!.Bytes);
        Assert.Equal(2, _posters.Requests.Count);
    }

    [Fact]
    public async Task A_poster_is_kept_for_an_hour()
    {
        var cache = Create();
        var id = Guid.NewGuid();

        await cache.GetAsync(id, Poster, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(59));
        await cache.GetAsync(id, Poster, CancellationToken.None);
        Assert.Single(_posters.Requests);

        _time.Advance(TimeSpan.FromMinutes(2));
        await cache.GetAsync(id, Poster, CancellationToken.None);
        Assert.Equal(2, _posters.Requests.Count);
    }

    [Fact]
    public async Task The_oldest_entries_go_beyond_the_entry_limit()
    {
        var cache = Create(maxEntries: 2);
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await cache.GetAsync(a, Poster, CancellationToken.None);
        await cache.GetAsync(b, Poster, CancellationToken.None);
        await cache.GetAsync(c, Poster, CancellationToken.None);
        await cache.GetAsync(c, Poster, CancellationToken.None);
        await cache.GetAsync(b, Poster, CancellationToken.None);
        Assert.Equal(3, _posters.Requests.Count);

        await cache.GetAsync(a, Poster, CancellationToken.None);
        Assert.Equal(4, _posters.Requests.Count);
        Assert.True(cache.Count <= 2);
    }

    [Fact]
    public async Task The_oldest_entries_go_beyond_the_byte_limit()
    {
        var cache = Create(maxBytes: 10);
        _posters.Image = new PosterImage(new byte[4], "image/jpeg");
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await cache.GetAsync(a, Poster, CancellationToken.None);
        await cache.GetAsync(b, Poster, CancellationToken.None);
        await cache.GetAsync(c, Poster, CancellationToken.None); // 12 bytes: a goes

        Assert.True(cache.Bytes <= 10);
        await cache.GetAsync(c, Poster, CancellationToken.None);
        await cache.GetAsync(b, Poster, CancellationToken.None);
        Assert.Equal(3, _posters.Requests.Count);
        await cache.GetAsync(a, Poster, CancellationToken.None);
        Assert.Equal(4, _posters.Requests.Count);
    }
}
