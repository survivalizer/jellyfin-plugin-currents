namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A playable URL, or the reason none was found.</summary>
/// <param name="Url">The playable URL, when resolution succeeded.</param>
/// <param name="Error">The reason resolution failed, when it did.</param>
public sealed record ResolveResult(Uri? Url, string? Error)
{
    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The failure reason.</param>
    /// <returns>A result with no URL.</returns>
    public static ResolveResult Fail(string error) => new(null, error);
}
