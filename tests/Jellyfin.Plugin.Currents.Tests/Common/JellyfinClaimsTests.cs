using System.Security.Claims;
using Jellyfin.Plugin.Currents.Common;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Common;

public class JellyfinClaimsTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "Custom"));

    [Fact]
    public void Reads_the_user_id_claim_case_insensitively()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, JellyfinClaims.GetUserId(Principal(new Claim("jellyfin-userid", id.ToString("N")))));
    }

    [Fact]
    public void Missing_or_bad_claims_are_empty()
    {
        Assert.Equal(Guid.Empty, JellyfinClaims.GetUserId(null));
        Assert.Equal(Guid.Empty, JellyfinClaims.GetUserId(Principal()));
        Assert.Equal(Guid.Empty, JellyfinClaims.GetUserId(Principal(new Claim(JellyfinClaims.UserIdClaim, "nope"))));
    }

    [Fact]
    public void Detects_api_keys()
    {
        Assert.True(JellyfinClaims.IsApiKey(Principal(new Claim(JellyfinClaims.IsApiKeyClaim, "True"))));
        Assert.False(JellyfinClaims.IsApiKey(Principal(new Claim(JellyfinClaims.IsApiKeyClaim, "False"))));
        Assert.False(JellyfinClaims.IsApiKey(null));
    }

    [Fact]
    public void Reads_the_access_token()
    {
        Assert.Equal("abc123", JellyfinClaims.GetToken(Principal(new Claim(JellyfinClaims.TokenClaim, "abc123"))));
        Assert.Null(JellyfinClaims.GetToken(new ClaimsPrincipal()));
        Assert.Null(JellyfinClaims.GetToken(null));
    }
}
