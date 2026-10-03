using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class TrackMatcherTests
{
    private static MediaStream Audio(int index, string? language, bool forced = false) =>
        new() { Type = MediaStreamType.Audio, Index = index, Language = language, IsForced = forced };

    private static MediaStream Sub(int index, string? language, bool forced = false) =>
        new() { Type = MediaStreamType.Subtitle, Index = index, Language = language, IsForced = forced };

    [Fact]
    public void Same_language_maps_even_when_the_real_order_differs()
    {
        MediaStream[] display = [Audio(501, "eng"), Audio(502, "fre")];
        MediaStream[] probed = [Audio(1, "fra"), Audio(2, "eng")];

        Assert.Equal(1, TrackMatcher.Map(display, probed, 502));
        Assert.Equal(2, TrackMatcher.Map(display, probed, 501));
    }

    [Fact]
    public void The_second_track_of_a_language_maps_to_the_second_real_one()
    {
        MediaStream[] display = [Audio(501, "eng"), Audio(502, "eng")];
        MediaStream[] probed = [Audio(1, "eng"), Audio(2, "eng")];

        Assert.Equal(2, TrackMatcher.Map(display, probed, 502));
    }

    [Fact]
    public void Forced_subtitles_prefer_forced_real_tracks()
    {
        MediaStream[] display = [Sub(503, "eng", forced: true), Sub(504, "eng")];
        MediaStream[] probed = [Sub(3, "eng"), Sub(4, "eng", forced: true)];

        Assert.Equal(4, TrackMatcher.Map(display, probed, 503));
        Assert.Equal(3, TrackMatcher.Map(display, probed, 504));
    }

    [Fact]
    public void Without_a_language_the_position_maps_only_when_the_counts_agree()
    {
        MediaStream[] display = [Audio(501, null), Audio(502, null)];

        Assert.Equal(2, TrackMatcher.Map(display, [Audio(1, null), Audio(2, null)], 502));
        Assert.Null(TrackMatcher.Map(display, [Audio(1, null)], 502));
    }

    [Fact]
    public void A_language_the_file_lacks_or_an_unknown_index_maps_to_nothing()
    {
        MediaStream[] display = [Audio(501, "eng"), Audio(502, "ger")];
        MediaStream[] probed = [Audio(1, "eng")];

        Assert.Null(TrackMatcher.Map(display, probed, 502));
        Assert.Null(TrackMatcher.Map(display, probed, 777));
    }
}
