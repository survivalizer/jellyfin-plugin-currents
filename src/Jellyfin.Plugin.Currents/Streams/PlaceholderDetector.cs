namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Recognises AIOStreams' "error as a video" responses (e.g. /static/downloading.mp4) and upstream slate videos (…/slate.mp4).</summary>
public static class PlaceholderDetector
{
    private static readonly string[] KnownNames =
    [
        "downloading", "no_matching_file", "payment_required", "content_proxy_limit_reached",
        "401", "403", "404", "429", "500", "502", "503",
    ];

    /// <summary>Checks whether the final (post-redirect) URI is a placeholder video.</summary>
    /// <param name="finalUri">The URI the playback request ended up at.</param>
    /// <param name="aioStreamsBase">The configured AIOStreams base URI.</param>
    /// <param name="requestedUri">The AIOStreams playback URI the resolver started from (pre-redirect), if known.</param>
    /// <returns>True when the final URI is a placeholder.</returns>
    public static bool IsPlaceholder(Uri finalUri, Uri aioStreamsBase, Uri? requestedUri = null)
    {
        var path = finalUri.AbsolutePath;

        // Upstream addons (e.g. Comet on ElfHosted) redirect uncached titles to a rendered "slate" video.
        if (string.Equals(Path.GetFileName(path), "slate.mp4", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!path.Contains("/static/", StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var basePath = aioStreamsBase.AbsolutePath;
        if (!basePath.EndsWith('/'))
        {
            basePath += "/";
        }

        if (SameAuthority(finalUri, aioStreamsBase)
            && path.StartsWith(basePath + "static/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (requestedUri is not null && SameAuthority(finalUri, requestedUri))
        {
            return true;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return KnownNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameAuthority(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;
}
