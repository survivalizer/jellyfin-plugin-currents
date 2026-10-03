using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class RemuxDbCacheTests
{
    private const string Hash = "f1c8b4ba4ae611494ffb09958241165aaf1f07db";
    private static readonly CurrentsTitle Episode = new("series", "tt0903747:1:1");
    private readonly FakeSettings _settings = new();
    private readonly FakeRemuxDbClient _client = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private static StreamResult Stream() => new() { InfoHash = Hash, FileIdx = 0, Filename = "Show.S01E01.mkv" };

    private RemuxDbCache Create() => new(_client, _settings, _time, NullLogger<RemuxDbCache>.Instance);

    [Fact]
    public async Task A_lookup_that_stalls_is_cut_off_by_the_total_budget_and_cached_as_an_error()
    {
        _client.Stall = true;
        var cache = new RemuxDbCache(_client, _settings, _time, NullLogger<RemuxDbCache>.Instance, TimeSpan.FromMilliseconds(100));

        await cache.WarmAsync(Episode, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await cache.WarmAsync(Episode, CancellationToken.None);

        Assert.Null(cache.Match(Episode, Stream()));
        Assert.Equal(1, _client.Calls);

        _time.Advance(TimeSpan.FromSeconds(61));
        await cache.WarmAsync(Episode, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, _client.Calls);
    }

    [Theory]
    [InlineData("movie", "tt0133093", "tt0133093")]
    [InlineData("movie", "tmdb:603", "tmdb:603")]
    [InlineData("series", "tt0903747:1:1", "tt0903747:1:1")]
    [InlineData("series", "tmdb:1396:2:3", "tmdb:1396:2:3")]
    [InlineData("movie", "kitsu:1", null)]
    [InlineData("series", "tt0903747", null)]
    [InlineData("series", "kitsu:1:5", null)]
    public void External_ids_for_movies_and_episodes(string type, string id, string? expected) =>
        Assert.Equal(expected, RemuxDbCache.ExternalId(new CurrentsTitle(type, id)));

    [Fact]
    public async Task Warmed_titles_match_streams_without_another_lookup()
    {
        var version = new RemuxDbVersion { Sources = [new RemuxDbSource { Kind = "torrent", TorrentInfoHash = Hash, TorrentFileIdx = 0 }] };
        _client.Versions["tt0903747:1:1"] = [version];
        var cache = Create();

        Assert.Null(cache.Match(Episode, Stream()));
        await Task.WhenAll(cache.WarmAsync(Episode, CancellationToken.None), cache.WarmAsync(Episode, CancellationToken.None));
        await cache.WarmAsync(Episode, CancellationToken.None);

        Assert.Same(version, cache.Match(Episode, Stream()));
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Failures_fall_back_quietly_and_retry_after_a_minute()
    {
        _client.Exception = new RemuxDbException("RemuxDB returned 502.");
        var cache = Create();

        await cache.WarmAsync(Episode, CancellationToken.None);
        await cache.WarmAsync(Episode, CancellationToken.None);
        Assert.Null(cache.Match(Episode, Stream()));
        Assert.Equal(1, _client.Calls);

        _time.Advance(TimeSpan.FromSeconds(61));
        await cache.WarmAsync(Episode, CancellationToken.None);
        Assert.Equal(2, _client.Calls);
    }

    [Fact]
    public async Task Unexpected_failures_also_fall_back_quietly_and_are_cached()
    {
        _client.Exception = new IOException("connection reset");
        var cache = Create();

        await cache.WarmAsync(Episode, CancellationToken.None);
        await cache.WarmAsync(Episode, CancellationToken.None);
        Assert.Null(cache.Match(Episode, Stream()));
        Assert.Equal(1, _client.Calls);

        _time.Advance(TimeSpan.FromSeconds(61));
        await cache.WarmAsync(Episode, CancellationToken.None);
        Assert.Equal(2, _client.Calls);
    }

    [Fact]
    public async Task Misses_are_kept_for_thirty_minutes()
    {
        var cache = Create();

        await cache.WarmAsync(Episode, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(29));
        await cache.WarmAsync(Episode, CancellationToken.None);
        Assert.Equal(1, _client.Calls);

        _time.Advance(TimeSpan.FromMinutes(2));
        await cache.WarmAsync(Episode, CancellationToken.None);
        Assert.Equal(2, _client.Calls);
    }

    [Fact]
    public async Task Switched_off_never_calls_remuxdb()
    {
        _settings.Current.EnableRemuxDb = false;
        var cache = Create();

        await cache.WarmAsync(Episode, CancellationToken.None);

        Assert.Equal(0, _client.Calls);
        Assert.Null(cache.Match(Episode, Stream()));
    }
}
