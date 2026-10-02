using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public class VersionProberTests
{
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly (IMediaSourceManager Instance, InterfaceFake Fake) _media = InterfaceFake.Create<IMediaSourceManager>();
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly ProbeCache _probes;
    private readonly ListLogger<VersionProber> _logger = new();

    public VersionProberTests() => _probes = new ProbeCache(_time);

    private VersionProber Create() =>
        new(_media.Instance, _library.Instance, _probes, new VersionSourceBuilder(_settings, _time, _probes), new FixedUrl("http://127.0.0.1:8096"), _time, _logger);

    private static VersionEntry Entry(StreamResult result) =>
        new(StreamIdentity.VersionId(Item, Alice, "k"), Item, Alice, new CurrentsTitle("movie", "tt1"), new RankedStream("k", result));

    private static StreamResult Unparsed() => new() { Url = "https://aio.example.com/play/1", Filename = "x.mkv" };

    private void ProbeFills(long runtime)
    {
        _media.Fake.On(nameof(IMediaSourceManager.AddMediaInfoWithProbe), args =>
        {
            var source = (MediaSourceInfo)args[0]!;
            source.RunTimeTicks = runtime;
            source.MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "h264" }, new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "aac" }];
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Unknown_streams_are_probed_once_through_the_internal_url_without_a_cache_key()
    {
        ProbeFills(TimeSpan.FromMinutes(90).Ticks);
        var prober = Create();
        var movie = new Movie { Id = Item, RunTimeTicks = TimeSpan.FromMinutes(90).Ticks };

        await prober.PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None);
        await prober.PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None);

        var call = Assert.Single(_media.Fake.Calls(nameof(IMediaSourceManager.AddMediaInfoWithProbe)));
        var probed = (MediaSourceInfo)call[0]!;
        Assert.StartsWith("http://127.0.0.1:8096/Currents/play/s/", probed.Path, StringComparison.Ordinal);
        Assert.Null(call[2]);
        Assert.Equal(5000, probed.AnalyzeDurationMs);
        Assert.True(_probes.TryGet("k", out var cached));
        Assert.Equal("h264", cached.Streams()[0].Codec);
    }

    [Fact]
    public async Task Well_described_streams_are_not_probed()
    {
        var described = new StreamResult
        {
            Url = "https://aio.example.com/play/1",
            Size = 4_000_000_000,
            Duration = 7_200_000,
            ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"], AudioChannels = ["2.0"] },
        };

        await Create().PrepareAsync(new Movie { Id = Item, RunTimeTicks = 1 }, Entry(described), CancellationToken.None);

        Assert.Empty(_media.Fake.Calls(nameof(IMediaSourceManager.AddMediaInfoWithProbe)));
    }

    [Fact]
    public async Task Concurrent_requests_share_one_probe()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _media.Fake.On(nameof(IMediaSourceManager.AddMediaInfoWithProbe), _ => gate.Task);
        var prober = Create();
        var movie = new Movie { Id = Item, RunTimeTicks = 1 };

        var pending = Enumerable.Range(0, 4).Select(_ => prober.PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None)).ToList();
        gate.SetResult();
        await Task.WhenAll(pending);

        Assert.Single(_media.Fake.Calls(nameof(IMediaSourceManager.AddMediaInfoWithProbe)));
    }

    [Fact]
    public async Task Probe_failures_are_logged_masked_and_not_retried_for_ten_minutes()
    {
        _media.Fake.On(nameof(IMediaSourceManager.AddMediaInfoWithProbe), _ =>
            Task.FromException(new InvalidOperationException("ffprobe failed for http://127.0.0.1:8096/Currents/play/s/SECRET.TOKEN")));
        var prober = Create();
        var movie = new Movie { Id = Item, RunTimeTicks = 1 };

        await prober.PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None);
        await prober.PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(11));
        await prober.PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None);

        Assert.Equal(2, _media.Fake.Calls(nameof(IMediaSourceManager.AddMediaInfoWithProbe)).Count);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains("SECRET", StringComparison.Ordinal));
        Assert.False(_probes.TryGet("k", out _));
    }

    [Fact]
    public async Task Missing_item_runtime_is_saved_from_the_probe()
    {
        ProbeFills(TimeSpan.FromMinutes(95).Ticks);
        var movie = new Movie { Id = Item };

        await Create().PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(95).Ticks, movie.RunTimeTicks);
        Assert.Single(_library.Fake.Calls(nameof(ILibraryManager.UpdateItemAsync)));
    }

    [Fact]
    public async Task Missing_item_runtime_is_saved_from_aiostreams_duration_without_probing()
    {
        var described = new StreamResult
        {
            Url = "https://aio.example.com/play/1",
            Size = 4_000_000_000,
            Duration = 7_200_000,
            ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"] },
        };
        var movie = new Movie { Id = Item };

        await Create().PrepareAsync(movie, Entry(described), CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(2).Ticks, movie.RunTimeTicks);
        Assert.Empty(_media.Fake.Calls(nameof(IMediaSourceManager.AddMediaInfoWithProbe)));
    }

    [Fact]
    public async Task Existing_item_runtime_is_left_alone()
    {
        ProbeFills(TimeSpan.FromMinutes(95).Ticks);
        var movie = new Movie { Id = Item, RunTimeTicks = TimeSpan.FromMinutes(90).Ticks };

        await Create().PrepareAsync(movie, Entry(Unparsed()), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(90).Ticks, movie.RunTimeTicks);
        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.UpdateItemAsync)));
    }

    private sealed class FixedUrl(string value) : IInternalBaseUrl
    {
        public string Value => value;
    }
}
