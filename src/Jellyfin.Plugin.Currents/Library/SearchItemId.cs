using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>
/// The id a search result carries before (and after) its title is in the library: HMAC-SHA256 of the state id under
/// the install secret. It stays the same across searches and restarts, cannot be computed from public ids without the
/// secret, and never equals a Jellyfin item id (those are MD5 of type + path).
/// </summary>
public static class SearchItemId
{
    /// <summary>Computes the search id of a title.</summary>
    /// <param name="secret">The install <c>SigningSecret</c> (base64).</param>
    /// <param name="stateId">The title's state id, e.g. "movie/tt123".</param>
    /// <returns>The search id.</returns>
    public static Guid For(string secret, string stateId)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var mac = HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes("currents/search/" + stateId));
        return new Guid(mac.AsSpan(0, 16));
    }
}
