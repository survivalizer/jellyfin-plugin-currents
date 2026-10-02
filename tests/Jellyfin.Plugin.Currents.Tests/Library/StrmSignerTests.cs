using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class StrmSignerTests
{
    private readonly StrmSigner _signer = new(StrmSigner.NewSecret());

    [Fact]
    public void Verifies_its_own_signature()
    {
        var sig = _signer.Sign("movie", "tt0111161");

        Assert.True(_signer.Verify("movie", "tt0111161", sig));
    }

    [Theory]
    [InlineData("series", "tt0111161")]
    [InlineData("movie", "tt0111162")]
    public void Rejects_signature_for_a_different_title(string type, string id)
    {
        var sig = _signer.Sign("movie", "tt0111161");

        Assert.False(_signer.Verify(type, id, sig));
    }

    [Fact]
    public void Rejects_missing_tampered_or_foreign_signatures()
    {
        var sig = _signer.Sign("movie", "tt1");
        var other = new StrmSigner(StrmSigner.NewSecret());

        Assert.False(_signer.Verify("movie", "tt1", null));
        Assert.False(_signer.Verify("movie", "tt1", string.Empty));
        Assert.False(_signer.Verify("movie", "tt1", sig[..^1] + (sig[^1] == 'A' ? 'B' : 'A')));
        Assert.False(other.Verify("movie", "tt1", sig));
    }

    [Fact]
    public void Builds_strm_url_with_escaped_episode_id()
    {
        var url = _signer.StrmUrl("http://127.0.0.1:8096/", "series", "tt0944947:1:2");

        Assert.StartsWith("http://127.0.0.1:8096/Currents/play/series/tt0944947%3A1%3A2?sig=", url, StringComparison.Ordinal);
        var sig = url[(url.IndexOf("sig=", StringComparison.Ordinal) + 4)..];
        Assert.True(_signer.Verify("series", "tt0944947:1:2", sig));
    }

    [Fact]
    public void Rejects_empty_secret()
    {
        Assert.Throws<ArgumentException>(() => new StrmSigner(string.Empty));
    }
}
