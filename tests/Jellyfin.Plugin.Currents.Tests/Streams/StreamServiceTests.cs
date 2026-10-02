using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamServiceTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeAioStreamsClient _client = new();
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);

    public StreamServiceTests()
    {
        _client.Outcome = new SearchOutcome(
            [Result("a", "720p"), Result("b", "2160p"), Result("c", "1080p")],
            []);
    }

    private static StreamResult Result(string name, string res) =>
        new() { Url = $"https://cdn.example.com/{name}", Filename = name, ParsedFile = new ParsedFile { Resolution = res } };

    private static StreamProfile Profile(string password = "pw", StreamPreferences? prefs = null)
    {
        AioStreamsCredentials.TryParse($"https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/{password}/manifest.json", out var creds, out _);
        return new StreamProfile(ProfileSource.Default, creds, prefs ?? new StreamPreferences(), AutoSelect: false, Disabled: false);
    }

    private StreamService Create() => new(_client, _settings, _time, NullLogger<StreamService>.Instance);

    private static string[] Names(StreamLookup lookup) => lookup.Streams.Select(s => s.Result.Filename!).ToArray();

    [Fact]
    public async Task Ranks_with_the_profile_preferences_and_keeps_keys_stable()
    {
        var service = Create();

        var plain = await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        var ranked = await service.GetAsync(Profile(prefs: new StreamPreferences { ResolutionOrder = ["2160p", "1080p"] }), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Null(plain.Error);
        Assert.Equal(new[] { "a", "b", "c" }, Names(plain));
        Assert.Equal(new[] { "b", "c", "a" }, Names(ranked));
        Assert.Equal(plain.Streams.Single(s => s.Result.Filename == "b").Key, ranked.Streams[0].Key);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Different_configs_do_not_share_results()
    {
        var service = Create();

        await service.GetAsync(Profile("one"), "movie", "tt1", Wait, CancellationToken.None);
        await service.GetAsync(Profile("two"), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Equal(2, _client.Calls);
        Assert.Equal("two", _client.LastCredentials!.Password);
    }

    [Fact]
    public async Task Cache_expires_after_stream_cache_minutes()
    {
        _settings.Current.StreamCacheMinutes = 60;
        var service = Create();
        await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(61));
        await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Equal(2, _client.Calls);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_search()
    {
        _client.Gate = new TaskCompletionSource<SearchOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Create();

        var first = service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        var second = service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        _client.Gate.SetResult(_client.Outcome);

        Assert.Equal(3, (await first).Streams.Count);
        Assert.Equal(3, (await second).Streams.Count);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Slow_search_returns_a_retry_message_and_still_fills_the_cache()
    {
        _client.Gate = new TaskCompletionSource<SearchOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Create();

        var slow = await service.GetAsync(Profile(), "movie", "tt1", TimeSpan.FromMilliseconds(50), CancellationToken.None);
        _client.Gate.SetResult(_client.Outcome);
        await Task.Delay(50);
        var later = await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Empty(slow.Streams);
        Assert.Contains("taking a while", slow.Error, StringComparison.Ordinal);
        Assert.Equal(3, later.Streams.Count);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_cancel_the_shared_search()
    {
        _client.Gate = new TaskCompletionSource<SearchOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Create();
        using var cts = new CancellationTokenSource();

        var cancelled = service.GetAsync(Profile(), "movie", "tt1", Wait, cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        _client.Gate.SetResult(_client.Outcome);
        await Task.Delay(50);

        Assert.Equal(3, service.Peek(Profile(), "movie", "tt1")!.Streams.Count);
    }

    [Fact]
    public async Task Failures_are_cached_briefly_and_masked()
    {
        _client.Exception = new AioStreamsException("Upstream said no for https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json");
        var service = Create();

        var first = await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        var second = await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(31));
        _client.Exception = null;
        var third = await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.StartsWith("AIOStreams search failed", first.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("0b6c3c7e", first.Error, StringComparison.Ordinal);
        Assert.Equal(first.Error, second.Error);
        Assert.Null(third.Error);
        Assert.Equal(2, _client.Calls);
    }

    [Fact]
    public async Task Open_circuit_is_reported_as_a_failure()
    {
        _client.Exception = new CircuitOpenException();

        var lookup = await Create().GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.StartsWith("AIOStreams search failed", lookup.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_and_unconfigured_profiles_never_search()
    {
        var service = Create();
        var disabled = new StreamProfile(ProfileSource.None, null, new StreamPreferences(), false, Disabled: true);
        var none = new StreamProfile(ProfileSource.None, null, new StreamPreferences(), false, Disabled: false);

        var a = await service.GetAsync(disabled, "movie", "tt1", Wait, CancellationToken.None);
        var b = await service.GetAsync(none, "movie", "tt1", Wait, CancellationToken.None);

        Assert.Contains("disabled", a.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not configured", b.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _client.Calls);
    }

    [Fact]
    public async Task No_results_is_an_error_message_with_an_empty_list()
    {
        _client.Outcome = new SearchOutcome([], ["Torrentio: Timed out"]);

        var lookup = await Create().GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Empty(lookup.Streams);
        Assert.Equal("No streams found for this title.", lookup.Error);
    }

    [Fact]
    public async Task Peek_never_searches()
    {
        var service = Create();

        Assert.Null(service.Peek(Profile(), "movie", "tt1"));
        await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Equal(3, service.Peek(Profile(), "movie", "tt1")!.Streams.Count);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Empty_results_with_addon_errors_are_cached_only_briefly()
    {
        _client.Outcome = new SearchOutcome([], ["Torrentio: Timed out"]);
        var service = Create();

        var first = await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        Assert.Equal(1, _client.Calls);
        _time.Advance(TimeSpan.FromSeconds(31));
        await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Equal("No streams found for this title.", first.Error);
        Assert.Equal(2, _client.Calls);
    }

    [Fact]
    public async Task Empty_results_without_errors_keep_the_full_cache_ttl()
    {
        _client.Outcome = new SearchOutcome([], []);
        var service = Create();

        await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(31));
        await service.GetAsync(Profile(), "movie", "tt1", Wait, CancellationToken.None);

        Assert.Equal(1, _client.Calls);
    }
}
