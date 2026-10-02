using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class PlaceholderDetectorTests
{
    private static readonly Uri AioBase = new("https://aio.example.com/");

    [Theory]
    [InlineData("https://aio.example.com/static/downloading.mp4", true)]
    [InlineData("https://aio.example.com/static/some_new_error.mp4", true)]
    [InlineData("https://proxy.example.com/static/payment_required.mp4", true)]
    [InlineData("https://proxy.example.com/static/no_matching_file.mp4", true)]
    [InlineData("https://cdn.example.com/static/holiday-video.mp4", false)]
    [InlineData("https://cdn.real-debrid.com/d/ABC/Movie.2160p.mkv", false)]
    [InlineData("https://aio.example.com/api/v1/debrid/playback/x/y.mkv", false)]
    public void Detects_aiostreams_placeholder_videos(string url, bool expected)
    {
        Assert.Equal(expected, PlaceholderDetector.IsPlaceholder(new Uri(url), AioBase));
    }

    [Fact]
    public void Detects_placeholder_behind_proxy_using_requested_uri()
    {
        var final = new Uri("https://proxy.example.com/static/some_new_error.mp4");
        var aio = new Uri("https://aio.internal:3000/");
        var requested = new Uri("https://proxy.example.com/api/v1/debrid/playback/x/y.mkv");

        Assert.True(PlaceholderDetector.IsPlaceholder(final, aio, requested));
        Assert.False(PlaceholderDetector.IsPlaceholder(final, aio));
    }

    [Fact]
    public void Same_host_different_port_is_not_a_placeholder()
    {
        var final = new Uri("https://aio.example.com:8443/static/promo.mp4");

        Assert.False(PlaceholderDetector.IsPlaceholder(final, AioBase));
    }

    [Theory]
    [InlineData("https://host.example.com/aio/static/downloading_v2.mp4", true)]
    [InlineData("https://host.example.com/other/static/x.mp4", false)]
    public void Anchors_static_path_to_base_path(string url, bool expected)
    {
        var aio = new Uri("https://host.example.com/aio/");

        Assert.Equal(expected, PlaceholderDetector.IsPlaceholder(new Uri(url), aio));
    }

    [Theory]
    [InlineData("https://slate.elfhosted.com/cache/3cb11ad9c7db/slate.mp4?preset=default&title=Still%20downloading", true)]
    [InlineData("https://slate.example.org/SLATE.MP4", true)]
    [InlineData("https://store-043.wnam.tb-cdn.io/dl/abc/slate.mkv", false)]
    [InlineData("https://cdn.example.com/d/ABC/Slate.Movie.2019.1080p.mp4", false)]
    public void Detects_slate_placeholder_videos_from_upstream_addons(string url, bool expected)
    {
        Assert.Equal(expected, PlaceholderDetector.IsPlaceholder(new Uri(url), AioBase));
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var final = new Uri("https://aio.example.com/STATIC/DOWNLOADING.MP4");

        Assert.True(PlaceholderDetector.IsPlaceholder(final, AioBase));
    }
}
