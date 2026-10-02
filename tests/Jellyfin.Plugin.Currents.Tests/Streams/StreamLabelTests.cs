using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamLabelTests
{
    [Fact]
    public void Full_label_lists_quality_size_cache_state_and_addon()
    {
        var stream = new StreamResult
        {
            Size = 18_400_000_000,
            Cached = true,
            Addon = "Torrentio",
            ParsedFile = new ParsedFile { Resolution = "2160p", Encode = "HEVC", VisualTags = ["DV", "10bit"], AudioTags = ["Atmos", "TrueHD"] },
        };

        Assert.Equal("2160p DV · HEVC · Atmos · 18.4 GB · cached · Torrentio", StreamLabel.For(stream));
    }

    [Fact]
    public void Unknown_values_are_skipped()
    {
        var stream = new StreamResult { Cached = false, ParsedFile = new ParsedFile { Resolution = "Unknown", Encode = "Unknown", AudioTags = ["Unknown", "DD+"] } };

        Assert.Equal("DD+ · uncached", StreamLabel.For(stream));
    }

    [Fact]
    public void Falls_back_to_file_name_then_generic_name()
    {
        Assert.Equal("Movie.2020.mkv", StreamLabel.For(new StreamResult { Filename = "Movie.2020.mkv" }));
        Assert.Equal("Stream", StreamLabel.For(new StreamResult()));
    }

    [Theory]
    [InlineData(18_400_000_000L, "18.4 GB")]
    [InlineData(1_000_000_000L, "1.0 GB")]
    [InlineData(850_000_000L, "850 MB")]
    [InlineData(1_000L, "1 MB")]
    public void Sizes_use_decimal_units(long bytes, string expected) =>
        Assert.Equal(expected, StreamLabel.FormatSize(bytes));
}
