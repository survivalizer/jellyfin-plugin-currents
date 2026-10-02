using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class VersionSourceBuilderTests
{
    private const string Internal = "http://127.0.0.1:8096/jf";
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly ProbeCache _probes;

    public VersionSourceBuilderTests() => _probes = new ProbeCache(_time);

    private static VersionEntry Entry(StreamResult? result = null)
    {
        result ??= new StreamResult
        {
            Url = "https://aio.example.com/api/v1/debrid/playback/SECRET/x.mkv",
            Filename = "Movie.2160p.mkv",
            Size = 18_400_000_000,
            Cached = true,
            Addon = "Torrentio",
            Duration = 7_200_000,
            ParsedFile = new ParsedFile { Resolution = "2160p", Encode = "HEVC", VisualTags = ["HDR10"], AudioTags = ["DD+"], AudioChannels = ["5.1"] },
        };
        const string key = "0123456789abcdef0123456789abcdef";
        return new VersionEntry(StreamIdentity.VersionId(Item, Alice, key), Item, Alice, new CurrentsTitle("movie", "tt1"), new RankedStream(key, result));
    }

    private VersionSourceBuilder Create() => new(_settings, _time, _probes);

    private static VersionContext Context(bool redact = false, bool remux = true, bool transcode = true, long? itemRuntime = null) =>
        new(Internal, redact, itemRuntime, remux, transcode);

    [Fact]
    public void Builds_a_streamed_never_direct_played_http_source()
    {
        var entry = Entry();

        var source = Create().Build(entry, Context());

        Assert.Equal(entry.VersionId, source.Id);
        Assert.Equal("2160p HDR10 · HEVC · DD+ · 18.4 GB · cached · Torrentio", source.Name);
        Assert.Equal(MediaProtocol.Http, source.Protocol);
        Assert.True(source.IsRemote);
        Assert.False(source.SupportsDirectPlay);
        Assert.True(source.SupportsDirectStream);
        Assert.True(source.SupportsTranscoding);
        Assert.False(source.RequiresOpening);
        Assert.Equal("mkv", source.Container);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, source.RunTimeTicks);
        Assert.Equal(18_400_000_000, source.Size);
        Assert.Equal(new[] { MediaStreamType.Video, MediaStreamType.Audio }, source.MediaStreams.Select(s => s.Type));
    }

    [Fact]
    public void Internal_path_carries_a_token_for_this_users_stream()
    {
        var entry = Entry();

        var source = Create().Build(entry, Context());

        Assert.StartsWith($"{Internal}/Currents/play/s/", source.Path, StringComparison.Ordinal);
        var token = source.Path[(Internal.Length + "/Currents/play/s/".Length)..];
        Assert.True(new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryRead(token, out var ticket));
        Assert.Equal(entry.Ticket, ticket);
        Assert.DoesNotContain("SECRET", source.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Token_lives_for_the_configured_hours()
    {
        _settings.Current.VersionTokenHours = 2;
        var source = Create().Build(Entry(), Context());
        var token = source.Path[(Internal.Length + "/Currents/play/s/".Length)..];

        _time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

        Assert.False(new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryRead(token, out _));
    }

    [Fact]
    public void Client_facing_sources_have_a_redacted_path()
    {
        var source = Create().Build(Entry(), Context(redact: true));

        Assert.Equal($"currents://version/{Entry().VersionId}", source.Path);
    }

    [Fact]
    public void User_permissions_switch_off_remux_and_transcode()
    {
        var source = Create().Build(Entry(), Context(remux: false, transcode: false));

        Assert.False(source.SupportsDirectStream);
        Assert.False(source.SupportsTranscoding);
    }

    [Fact]
    public void Probe_results_replace_the_prefill()
    {
        var entry = Entry();
        _probes.Set(entry.Stream.Key, ProbedMedia.From(new MediaSourceInfo
        {
            Container = "mp4",
            RunTimeTicks = 999,
            Bitrate = 1234,
            MediaStreams =
            [
                new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "h264" },
                new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "aac" },
                new MediaStream { Type = MediaStreamType.Audio, Index = 2, Codec = "ac3", Language = "jpn" },
            ],
        }));

        var source = Create().Build(entry, Context());

        Assert.Equal("mp4", source.Container);
        Assert.Equal(999, source.RunTimeTicks);
        Assert.Equal(1234, source.Bitrate);
        Assert.Equal(new[] { 0, 1, 2 }, source.MediaStreams.Select(s => s.Index));
    }

    [Fact]
    public void Item_runtime_is_the_fallback_runtime()
    {
        var entry = Entry(new StreamResult { Url = "https://x/a", Size = 1_000_000_000, ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC" } });

        var source = Create().Build(entry, Context(itemRuntime: TimeSpan.FromMinutes(42).Ticks));

        Assert.Equal(TimeSpan.FromMinutes(42).Ticks, source.RunTimeTicks);
    }

    [Fact]
    public void Every_call_returns_new_objects()
    {
        var builder = Create();

        var first = builder.Build(Entry(), Context());
        var second = builder.Build(Entry(), Context());

        Assert.NotSame(first, second);
        Assert.NotSame(first.MediaStreams[0], second.MediaStreams[0]);
    }

    [Fact]
    public void Notice_is_named_after_the_reason_and_cannot_play()
    {
        var notice = VersionSourceBuilder.Notice(Item, "Streams are not configured.");

        Assert.Equal(Item.ToString("N"), notice.Id);
        Assert.Equal("Streams are not configured.", notice.Name);
        Assert.False(notice.SupportsDirectPlay || notice.SupportsDirectStream || notice.SupportsTranscoding);
        Assert.Empty(notice.MediaStreams);
    }

    [Fact]
    public void Pending_uses_the_item_id()
    {
        Assert.Equal(Item.ToString("N"), VersionSourceBuilder.Pending(Item).Id);
    }
}
