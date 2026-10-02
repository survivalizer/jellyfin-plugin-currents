using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Removes credentials from URLs before they reach logs or error messages.</summary>
public static partial class SecretMasker
{
    public static string Mask(Uri? uri) => Mask(uri?.ToString());

    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var masked = StremioUuidAndPassword().Replace(value, "/stremio/***/***/");
        masked = StremioUuid().Replace(masked, "/stremio/***/");
        masked = PlaybackToken().Replace(masked, "/api/v1/${kind}/***/");
        masked = ResolveKey().Replace(masked, "/resolve/${service}/***");
        masked = PlaybackBlob().Replace(masked, "/playback/***");
        return QuerySecret().Replace(masked, "${key}=***");
    }

    // /stremio/{uuid}/{password}/ — the password segment is anything but a known route name.
    [GeneratedRegex(@"/stremio/[0-9a-fA-F-]{36}/(?!manifest\.json|catalog/|meta/|stream/|subtitles/|configure)[^/?#]+/", RegexOptions.CultureInvariant)]
    private static partial Regex StremioUuidAndPassword();

    [GeneratedRegex(@"/stremio/[0-9a-fA-F-]{36}/", RegexOptions.CultureInvariant)]
    private static partial Regex StremioUuid();

    [GeneratedRegex(@"/api/v1/(?<kind>debrid/playback|proxy)/[^/?#]+/", RegexOptions.CultureInvariant)]
    private static partial Regex PlaybackToken();

    // Wrapped addons such as Torrentio put the debrid API key after the service: /resolve/realdebrid/{key}/...
    [GeneratedRegex(@"/resolve/(?<service>[A-Za-z0-9_.-]+)/[^/?#\s]+(?=[/?#\s]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ResolveKey();

    // Encoded playback tokens (base64/base64url) that often embed credentials: /playback/{long token}
    [GeneratedRegex(@"/playback/[A-Za-z0-9_=-]{20,}(?=[/?#\s]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex PlaybackBlob();

    [GeneratedRegex(@"(?<key>sig|token|apikey|api_key|password)=[^&#]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QuerySecret();
}
