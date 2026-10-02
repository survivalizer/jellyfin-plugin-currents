using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class PruningPolicyTests
{
    [Theory]
    [InlineData(3, false, false, 3, true)]
    [InlineData(2, false, false, 3, false)]
    [InlineData(5, true, false, 3, false)]
    [InlineData(5, false, true, 3, false)]
    [InlineData(1, false, false, 0, true)]
    public void Prunes_only_unwatched_catalog_titles_past_the_threshold(int misses, bool addedBySearch, bool played, int threshold, bool expected)
    {
        var title = new TitleState { MissCount = misses, AddedBySearch = addedBySearch };

        Assert.Equal(expected, PruningPolicy.ShouldPrune(title, threshold, played));
    }
}
