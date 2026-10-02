using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class SearchItemIdTests
{
    private static readonly string Secret = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string OtherSecret = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void Is_the_first_16_bytes_of_the_namespaced_hmac_under_the_install_secret()
    {
        var mac = HMACSHA256.HashData(Convert.FromBase64String(Secret), Encoding.UTF8.GetBytes("currents/search/movie/tt0111161"));

        Assert.Equal(new Guid(mac.AsSpan(0, 16)), SearchItemId.For(Secret, "movie/tt0111161"));
    }

    [Fact]
    public void Is_stable_and_distinct_per_state_id()
    {
        Assert.Equal(SearchItemId.For(Secret, "series/tt1"), SearchItemId.For(Secret, "series/tt1"));
        Assert.NotEqual(SearchItemId.For(Secret, "series/tt1"), SearchItemId.For(Secret, "movie/tt1"));
        Assert.NotEqual(Guid.Empty, SearchItemId.For(Secret, "movie/tt1"));
    }

    [Fact]
    public void Differs_per_secret_so_it_cannot_be_computed_from_public_ids()
    {
        Assert.NotEqual(SearchItemId.For(Secret, "movie/tt1"), SearchItemId.For(OtherSecret, "movie/tt1"));
        Assert.NotEqual(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("currents/search/movie/tt1")).AsSpan(0, 16)), SearchItemId.For(Secret, "movie/tt1"));
    }
}
