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
}
