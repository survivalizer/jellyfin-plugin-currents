namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Recognises AIOStreams' "error as a video" responses (e.g. /static/downloading.mp4).</summary>
public static class PlaceholderDetector
{
    private static readonly string[] KnownNames =
    [
        "downloading", "no_matching_file", "payment_required", "content_proxy_limit_reached",
        "401", "403", "404", "429", "500", "502", "503",
    ];

    public static bool IsPlaceholder(Uri finalUri, Uri aioStreamsBase)
    {
        var path = finalUri.AbsolutePath;
        if (!path.Contains("/static/", StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(finalUri.Host, aioStreamsBase.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return KnownNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
