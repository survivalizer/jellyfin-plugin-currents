using System.Security.Claims;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Reads Jellyfin's authentication claims. Mirrors Jellyfin.Api's InternalClaimTypes, which plugins cannot reference.</summary>
public static class JellyfinClaims
{
    public const string UserIdClaim = "Jellyfin-UserId";
    public const string IsApiKeyClaim = "Jellyfin-IsApiKey";

    public static Guid GetUserId(ClaimsPrincipal? user)
    {
        var value = Find(user, UserIdClaim);
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public static bool IsApiKey(ClaimsPrincipal? user) =>
        bool.TryParse(Find(user, IsApiKeyClaim), out var isApiKey) && isApiKey;

    private static string? Find(ClaimsPrincipal? user, string type) =>
        user?.Claims.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
}
