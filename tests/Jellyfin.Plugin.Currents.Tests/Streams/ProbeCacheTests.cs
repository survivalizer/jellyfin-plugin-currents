using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class ProbeCacheTests
{
    private static MediaSourceInfo Probed() => new()
    {
        Container = "mkv",
        RunTimeTicks = 123,
        Bitrate = 9_000_000,
        Size = 42,
        MediaStreams =
        [
            new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "hevc", ColorTransfer = "smpte2084", ColorPrimaries = "bt2020", ColorSpace = "bt2020nc" },
            new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "eac3", Channels = 6, Language = "eng" },
            new MediaStream { Type = MediaStreamType.Subtitle, Index = 2, Codec = "subrip", Language = "eng" },
        ],
    };

    [Fact]
    public void Round_trips_streams_as_new_objects()
    {
        var media = ProbedMedia.From(Probed());

        var first = media.Streams();
        var second = media.Streams();

        Assert.Equal(3, first.Count);
        Assert.Equal("hevc", first[0].Codec);
        Assert.Equal(0, first[0].Index);
        Assert.Equal(VideoRangeType.HDR10, first[0].VideoRangeType);
        Assert.Equal(6, first[1].Channels);
        Assert.NotSame(first[0], second[0]);
        Assert.Equal(("mkv", 123L, 9_000_000, 42L), (media.Container, media.RunTimeTicks!.Value, media.Bitrate!.Value, media.Size!.Value));
    }

    [Fact]
    public void Entries_expire_after_seven_days()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new ProbeCache(time);
        cache.Set("k", ProbedMedia.From(Probed()));

        Assert.True(cache.TryGet("k", out _));
        time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        Assert.False(cache.TryGet("k", out _));
    }
}
