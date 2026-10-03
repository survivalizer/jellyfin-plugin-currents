using System.Security.Claims;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Reads Jellyfin's authentication claims. Mirrors Jellyfin.Api's InternalClaimTypes, which plugins cannot reference.</summary>
public static class JellyfinClaims
{
    public const string UserIdClaim = "Jellyfin-UserId";
    public const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    public const string TokenClaim = "Jellyfin-Token";

    public static Guid GetUserId(ClaimsPrincipal? user)
    {
        var value = Find(user, UserIdClaim);
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public static bool IsApiKey(ClaimsPrincipal? user) =>
        bool.TryParse(Find(user, IsApiKeyClaim), out var isApiKey) && isApiKey;

    /// <summary>Gets the caller's access token, for URLs Jellyfin itself would sign with <c>?ApiKey=</c>. Never log it.</summary>
    /// <param name="user">The request's user.</param>
    /// <returns>The token, or null.</returns>
    public static string? GetToken(ClaimsPrincipal? user) => Find(user, TokenClaim) is { Length: > 0 } token ? token : null;

    private static string? Find(ClaimsPrincipal? user, string type) =>
        user?.Claims.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
}
