namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A playable URL (and the request headers it needs), or the reason none was found.</summary>
/// <param name="Url">The playable URL, when resolution succeeded.</param>
/// <param name="Error">The reason resolution failed, when it did.</param>
/// <param name="Headers">Request headers the URL needs; the version route then proxies instead of redirecting. Never sent to clients.</param>
public sealed record ResolveResult(Uri? Url, string? Error, IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The failure reason.</param>
    /// <returns>A result with no URL.</returns>
    public static ResolveResult Fail(string error) => new(null, error);
}
