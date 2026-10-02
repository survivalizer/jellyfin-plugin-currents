using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class SearchItemIdTests
{
    [Fact]
    public void Is_the_first_16_bytes_of_the_namespaced_sha256()
    {
        var expected = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("currents/search/movie/tt0111161")).AsSpan(0, 16));

        Assert.Equal(expected, SearchItemId.For("movie/tt0111161"));
    }

    [Fact]
    public void Is_stable_and_distinct_per_state_id()
    {
        Assert.Equal(SearchItemId.For("series/tt1"), SearchItemId.For("series/tt1"));
        Assert.NotEqual(SearchItemId.For("series/tt1"), SearchItemId.For("movie/tt1"));
        Assert.NotEqual(Guid.Empty, SearchItemId.For("movie/tt1"));
    }
}
