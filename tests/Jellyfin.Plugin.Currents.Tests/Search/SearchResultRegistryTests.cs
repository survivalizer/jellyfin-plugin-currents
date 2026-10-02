using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Search;

public class SearchResultRegistryTests
{
    [Fact]
    public void Keeps_results_for_24_hours()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var registry = new SearchResultRegistry(time);
        var result = SearchResults.For(new TitleKey(MediaKind.Movie, "imdb", "tt1"), new StremioMeta { Id = "tt1", Name = "A" }, "movie");

        registry.Add(result);
        time.Advance(TimeSpan.FromHours(23));
        Assert.True(registry.TryGet(result.Id, out var found));
        Assert.Same(result, found);

        time.Advance(TimeSpan.FromHours(2));
        Assert.False(registry.TryGet(result.Id, out _));
    }

    [Fact]
    public void Result_id_name_and_year_come_from_the_key_and_meta()
    {
        var result = SearchResults.For(new TitleKey(MediaKind.Series, "tmdb", "42"), new StremioMeta { Id = "tmdb:42", Name = "Show", ReleaseInfo = "2019-2021" }, "series");

        Assert.Equal(SearchItemId.For(FakeSettings.Secret, "series/tmdb:42"), result.Id);
        Assert.Equal("Show", result.Name);
        Assert.Equal(2019, result.Year);
    }
}
