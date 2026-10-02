using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class StrmUrlTests
{
    private static readonly StrmSigner Signer = new(Convert.ToBase64String(new byte[32]));

    [Theory]
    [InlineData("http://192.168.1.5:8097", "movie", "tt0111161")]
    [InlineData("http://192.168.1.5:8097/jellyfin/", "series", "tt0944947:1:2")]
    [InlineData("http://h", "series", "kitsu:12:5")]
    public void Round_trips_what_the_signer_writes(string baseUrl, string type, string id)
    {
        Assert.True(StrmUrl.TryParse(Signer.StrmUrl(baseUrl, type, id), out var parsedType, out var parsedId, out var sig));

        Assert.Equal(type, parsedType);
        Assert.Equal(id, parsedId);
        Assert.True(Signer.Verify(type, id, sig));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://h/Videos/abc/stream")]
    [InlineData("http://h/Currents/play/channel/x?sig=a")]
    [InlineData("http://h/Currents/play/movie?sig=a")]
    [InlineData("http://h/Currents/play/movie/a/b?sig=a")]
    public void Rejects_anything_else(string? value) =>
        Assert.False(StrmUrl.TryParse(value, out _, out _, out _));

    [Fact]
    public void Missing_signature_parses_with_null_signature()
    {
        Assert.True(StrmUrl.TryParse("http://h/Currents/play/movie/tt1", out _, out _, out var sig));
        Assert.Null(sig);
    }
}
