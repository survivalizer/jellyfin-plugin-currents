using System.Text;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class VersionTokenSignerTests
{
    private static readonly string Secret = Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly VersionTicket _ticket = new(Guid.Parse("11111111222233334444555555555555"), "series", "tt0944947:1:2", "0123456789abcdef0123456789abcdef");

    private VersionTokenSigner Signer(string? secret = null) => new(secret ?? Secret, _time);

    [Fact]
    public void Round_trips_a_ticket_and_is_url_safe()
    {
        var token = Signer().Create(_ticket, TimeSpan.FromHours(24));

        Assert.Matches("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$", token);
        Assert.True(Signer().TryRead(token, out var read));
        Assert.Equal(_ticket, read);
    }

    [Fact]
    public void Expired_tokens_are_rejected()
    {
        var token = Signer().Create(_ticket, TimeSpan.FromHours(24));

        _time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        Assert.False(Signer().TryRead(token, out _));
    }

    [Fact]
    public void Tampered_payload_or_signature_is_rejected()
    {
        var token = Signer().Create(_ticket, TimeSpan.FromHours(1));
        var other = Signer().Create(_ticket with { StremioId = "tt0944947:1:3" }, TimeSpan.FromHours(1));
        var (body, sig) = (token[..token.IndexOf('.', StringComparison.Ordinal)], token[(token.IndexOf('.', StringComparison.Ordinal) + 1)..]);
        var otherBody = other[..other.IndexOf('.', StringComparison.Ordinal)];

        Assert.False(Signer().TryRead($"{otherBody}.{sig}", out _));
        Assert.False(Signer().TryRead($"{body}.{(sig[0] == 'A' ? 'B' : 'A')}{sig[1..]}", out _));
        Assert.False(Signer().TryRead(body, out _));
    }

    [Fact]
    public void A_different_install_secret_is_rejected()
    {
        var token = Signer().Create(_ticket, TimeSpan.FromHours(1));

        Assert.False(Signer(StrmSigner.NewSecret()).TryRead(token, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("not-base64!.sig")]
    [InlineData("e30.AAAAAAAAAAAAAAAAAAAAAA")]
    public void Junk_is_rejected_without_throwing(string? token) =>
        Assert.False(Signer().TryRead(token, out _));

    [Fact]
    public void A_strm_signature_cannot_be_used_as_a_version_token()
    {
        var strmSig = new StrmSigner(Secret).Sign("series", "tt0944947:1:2");

        Assert.False(Signer().TryRead($"e30.{strmSig}", out _));
    }

    [Fact]
    public void Path_for_a_token_is_the_version_route()
    {
        Assert.Equal("Currents/play/s/abc.def", VersionTokenSigner.PathFor("abc.def"));
    }

    [Fact]
    public void Oversized_tokens_are_rejected()
    {
        Assert.False(Signer().TryRead(new string('A', 5000) + ".x", out _));
    }
}
