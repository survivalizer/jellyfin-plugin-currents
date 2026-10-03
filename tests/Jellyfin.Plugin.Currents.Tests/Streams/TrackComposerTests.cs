using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class TrackComposerTests
{
    private static StreamResult Release(params string[] languages) => new()
    {
        Url = "https://aio.example.com/play/1",
        Size = 4_000_000_000,
        Duration = 7_200_000,
        ParsedFile = new ParsedFile
        {
            Resolution = "1080p",
            Encode = "AVC",
            AudioTags = ["DD+"],
            AudioChannels = ["5.1"],
            Languages = languages.ToList(),
            Subtitles = ["English", "German"],
        },
    };

    private static RemuxDbVersion Remux() => new()
    {
        Container = "matroska,webm",
        Duration = 3480.5,
        Size = 2_000_000_000,
        Bitrate = 4_597_000,
        Tracks =
        [
            new RemuxDbTrack { Kind = "video", Idx = 0, Codec = "hevc", Width = 3840, Height = 2160, PixelFormat = "yuv420p10le", ColorTransfer = "smpte2084", ColorPrimaries = "bt2020", ColorSpace = "bt2020_nc", DvProfile = 8 },
            new RemuxDbTrack { Kind = "video", Idx = 6, Codec = "mjpeg" },
            new RemuxDbTrack { Kind = "audio", Idx = 1, Codec = "truehd", Language = "eng", Channels = 8, ChannelLayout = "7.1", IsDefault = true, Title = "Branding" },
            new RemuxDbTrack { Kind = "audio", Idx = 2, Codec = "ac3", Language = "fra", Channels = 6, ChannelLayout = "5.1(side)" },
            new RemuxDbTrack { Kind = "subtitle", Idx = 3, Codec = "hdmv_pgs_subtitle", Language = "eng", IsForced = true },
            new RemuxDbTrack { Kind = "subtitle", Idx = 4, Codec = "subrip", Language = "spa", IsExternal = true },
        ],
    };

    [Fact]
    public void Probed_tracks_win_for_both_views()
    {
        var probed = ProbedMedia.From(new MediaSourceInfo
        {
            Container = "mp4",
            RunTimeTicks = 99,
            MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "h264" }, new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "aac" }],
        });

        var tracks = TrackComposer.Compose(Release("English"), null, probed, Remux());

        Assert.Equal(TrackOrigin.Probe, tracks.Origin);
        Assert.Equal(new[] { 0, 1 }, tracks.Display.Select(s => s.Index));
        Assert.Equal(new[] { 0, 1 }, tracks.Playback.Select(s => s.Index));
        Assert.NotSame(tracks.Display[0], tracks.Playback[0]);
        Assert.Equal(("mp4", 99L), (tracks.Container, tracks.RunTimeTicks!.Value));
        Assert.False(tracks.NeedsProbe);
    }

    // Jellyfin only extracts (and only offers External delivery with conversion for) embedded subtitles flagged
    // SupportsExternalStream; its MediaSourceManager.GetMediaStreams sets the flag, a probe result does not.
    [Fact]
    public void Probed_subtitles_support_external_streams_like_jellyfin_library_streams()
    {
        var probed = ProbedMedia.From(new MediaSourceInfo
        {
            Container = "matroska,webm",
            MediaStreams =
            [
                new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "hevc" },
                new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "dts" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 2, Codec = "subrip" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 3, Codec = "ass" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 4, Codec = "PGSSUB" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 5, Codec = "DVDSUB" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 6, Codec = "DVBSUB" },
            ],
        });

        var tracks = TrackComposer.Compose(Release("English"), null, probed, null);

        foreach (var view in new[] { tracks.Playback, tracks.Display })
        {
            Assert.Equal(
                new[] { false, false, true, true, true, true, false },
                view.Select(s => s.SupportsExternalStream));
        }
    }

    [Fact]
    public void Remuxdb_tracks_are_shown_with_synthetic_indexes()
    {
        var tracks = TrackComposer.Compose(Release("English"), null, null, Remux());

        Assert.Equal(TrackOrigin.RemuxDb, tracks.Origin);
        Assert.Equal(new[] { 500, 501, 502, 503 }, tracks.Display.Select(s => s.Index));
        Assert.Equal(new[] { MediaStreamType.Video, MediaStreamType.Audio, MediaStreamType.Audio, MediaStreamType.Subtitle }, tracks.Display.Select(s => s.Type));
        var video = tracks.Display[0];
        Assert.Equal(("hevc", 3840, 10, "bt2020nc"), (video.Codec, video.Width!.Value, video.BitDepth!.Value, video.ColorSpace));
        Assert.Equal(VideoRangeType.DOVIWithHDR10, video.VideoRangeType);
        Assert.Equal(("eng", "7.1", (string?)null), (tracks.Display[1].Language, tracks.Display[1].ChannelLayout, tracks.Display[1].Title));
        Assert.Equal(("fre", "5.1"), (tracks.Display[2].Language, tracks.Display[2].ChannelLayout));
        Assert.Equal(("PGSSUB", true), (tracks.Display[3].Codec, tracks.Display[3].IsForced));
        Assert.Equal(TimeSpan.FromSeconds(3480.5).Ticks, tracks.RunTimeTicks);
        Assert.Equal(("mkv", 4_597_000, 2_000_000_000L), (tracks.Container, tracks.Bitrate!.Value, tracks.Size!.Value));
        Assert.True(tracks.NeedsProbe); // two audio tracks and an embedded subtitle
    }

    [Fact]
    public void Aiostreams_track_lists_are_shown_when_there_is_no_remuxdb_match()
    {
        var result = Release("English");
        result.ParsedFile!.AudioTracks = [new MediaTrack { Lang = "eng", Codec = "eac3", Channels = "5.1", Default = true }, new MediaTrack { Lang = "jpn", Codec = "aac", Channels = "2", Commentary = true }];
        result.ParsedFile.SubtitleTracks = [new MediaTrack { Lang = "eng", Codec = "subrip", Forced = true }];

        var tracks = TrackComposer.Compose(result, null, null, null);

        Assert.Equal(TrackOrigin.AioStreams, tracks.Origin);
        Assert.Equal(new[] { 500, 501, 502, 503 }, tracks.Display.Select(s => s.Index));
        Assert.Equal(("jpn", 2, "stereo", "Commentary"), (tracks.Display[2].Language, tracks.Display[2].Channels!.Value, tracks.Display[2].ChannelLayout, tracks.Display[2].Title));
        Assert.True(tracks.Display[1].IsDefault);
        Assert.Equal("subrip", tracks.Display[3].Codec);
    }

    [Fact]
    public void Release_name_languages_become_one_audio_track_each_plus_subtitle_guesses()
    {
        var tracks = TrackComposer.Compose(Release("English", "French", "Multi"), null, null, null);

        Assert.Equal(TrackOrigin.ReleaseName, tracks.Origin);
        Assert.Equal(new[] { MediaStreamType.Video, MediaStreamType.Audio, MediaStreamType.Audio, MediaStreamType.Subtitle, MediaStreamType.Subtitle }, tracks.Display.Select(s => s.Type));
        Assert.Equal(new[] { "eng", "fre" }, tracks.Display.Where(s => s.Type == MediaStreamType.Audio).Select(s => s.Language));
        Assert.Equal(new[] { "eng", "ger" }, tracks.Display.Where(s => s.Type == MediaStreamType.Subtitle).Select(s => s.Language));
        Assert.All(tracks.Display.Where(s => s.Type == MediaStreamType.Audio), a => Assert.Equal("eac3", a.Codec));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Playback_sources_never_carry_synthetic_indexes(bool remux, bool aioTracks)
    {
        var result = Release("English", "French");
        if (aioTracks)
        {
            result.ParsedFile!.AudioTracks = [new MediaTrack { Lang = "eng" }, new MediaTrack { Lang = "fra" }];
        }

        var tracks = TrackComposer.Compose(result, null, null, remux ? Remux() : null);

        Assert.Equal(new[] { MediaStreamType.Video, MediaStreamType.Audio }, tracks.Playback.Select(s => s.Type));
        Assert.All(tracks.Playback, s => Assert.Equal(-1, s.Index));
        Assert.DoesNotContain(tracks.Playback, s => TrackIndexes.IsSynthetic(s.Index));
    }

    [Fact]
    public void Remuxdb_stubs_carry_the_default_tracks_details()
    {
        var tracks = TrackComposer.Compose(Release("English"), null, null, Remux());

        Assert.Equal(("hevc", 3840), (tracks.Playback[0].Codec, tracks.Playback[0].Width!.Value));
        Assert.Equal(("truehd", 8), (tracks.Playback[1].Codec, tracks.Playback[1].Channels!.Value));
    }

    [Fact]
    public void A_single_well_described_track_needs_no_probe()
    {
        var result = Release("English");
        result.ParsedFile!.Subtitles = [];

        Assert.False(TrackComposer.Compose(result, null, null, null).NeedsProbe);
    }

    [Fact]
    public void Aiostreams_tracks_still_probe_for_dolby_vision_tags()
    {
        var result = Release("English");
        result.ParsedFile!.Subtitles = [];
        result.ParsedFile.VisualTags = ["DV"];
        result.ParsedFile.AudioTracks = [new MediaTrack { Lang = "eng", Codec = "eac3", Channels = "5.1" }];

        var tracks = TrackComposer.Compose(result, null, null, null);

        Assert.Equal(TrackOrigin.AioStreams, tracks.Origin);
        Assert.True(tracks.NeedsProbe);
    }

    [Fact]
    public void Aiostreams_track_without_channels_on_a_release_without_channels_asks_for_a_probe()
    {
        var result = Release("English");
        result.ParsedFile!.Subtitles = [];
        result.ParsedFile.AudioChannels = [];
        result.ParsedFile.AudioTracks = [new MediaTrack { Lang = "eng", Codec = "eac3" }];

        Assert.True(TrackComposer.Compose(result, null, null, null).NeedsProbe);
    }

    [Fact]
    public void Each_call_returns_new_objects()
    {
        var first = TrackComposer.Compose(Release("English"), null, null, Remux());
        var second = TrackComposer.Compose(Release("English"), null, null, Remux());

        Assert.NotSame(first.Display[0], second.Display[0]);
        Assert.NotSame(first.Playback[0], second.Playback[0]);
    }
}
