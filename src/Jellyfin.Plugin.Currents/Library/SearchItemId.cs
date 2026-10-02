using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>
/// The id a search result carries before (and after) its title is in the library. Derived from the state id, so it
/// stays the same across searches and restarts, and never equals a Jellyfin item id (those are MD5 of type + path).
/// </summary>
public static class SearchItemId
{
    public static Guid For(string stateId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("currents/search/" + stateId)).AsSpan(0, 16));
}
