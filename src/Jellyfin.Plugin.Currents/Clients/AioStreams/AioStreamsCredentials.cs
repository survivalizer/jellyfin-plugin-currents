using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Currents.Clients.AioStreams;

/// <summary>An AIOStreams user config (UUID + encrypted password) parsed from its manifest URL.</summary>
/// <param name="BaseUri">The server base URI, ending in a slash.</param>
/// <param name="Uuid">The user config UUID.</param>
/// <param name="Password">The encrypted config password.</param>
public sealed record AioStreamsCredentials(Uri BaseUri, string Uuid, string Password)
{
    /// <summary>Parses an AIOStreams manifest URL.</summary>
    /// <param name="manifestUrl">The manifest URL pasted by the admin.</param>
    /// <param name="credentials">The parsed credentials when successful.</param>
    /// <param name="error">A user-facing message when parsing fails.</param>
    /// <returns>True when the URL carries a UUID and password.</returns>
    public static bool TryParse(string? manifestUrl, [NotNullWhen(true)] out AioStreamsCredentials? credentials, out string? error)
    {
        credentials = null;
        error = "Paste the full AIOStreams manifest URL (https://…/stremio/{uuid}/{password}/manifest.json). Alias URLs (/stremio/u/…) are not supported.";
        if (!Uri.TryCreate(manifestUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.LastIndexOf(segments, "stremio");
        if (index < 0
            || segments.Length != index + 4
            || !Guid.TryParse(segments[index + 1], out _)
            || !string.Equals(segments[index + 3], "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = index == 0 ? "/" : "/" + string.Join('/', segments[..index]) + "/";
        var baseUri = new UriBuilder(uri) { Path = prefix, Query = string.Empty, Fragment = string.Empty }.Uri;
        credentials = new AioStreamsCredentials(baseUri, segments[index + 1], Uri.UnescapeDataString(segments[index + 2]));
        error = null;
        return true;
    }

    /// <summary>Builds the HTTP Basic authorization header.</summary>
    /// <returns>The header value.</returns>
    public AuthenticationHeaderValue BasicAuth() =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uuid}:{Password}")));

    /// <summary>Builds the search API URI.</summary>
    /// <param name="type">The Stremio type.</param>
    /// <param name="id">The item id.</param>
    /// <returns>The search URI.</returns>
    public Uri Search(string type, string id) =>
        new(BaseUri, $"api/v1/search?type={Uri.EscapeDataString(type)}&id={Uri.EscapeDataString(id)}");

    /// <summary>Builds the Stremio subtitles URI. The password is in the path: mask it before logging.</summary>
    /// <param name="type">The Stremio type.</param>
    /// <param name="id">The Stremio id (tt…, tt…:S:E).</param>
    /// <returns>The subtitles URI.</returns>
    public Uri Subtitles(string type, string id) =>
        new(BaseUri, $"stremio/{Uri.EscapeDataString(Uuid)}/{Uri.EscapeDataString(Password)}/subtitles/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(id)}.json");

    /// <summary>A short, non-reversible id for this config, for cache keys.</summary>
    /// <returns>24 lower-case hex characters.</returns>
    public string Fingerprint() =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{BaseUri}|{Uuid}|{Password}")).AsSpan(0, 12));

    /// <inheritdoc />
    public override string ToString() => $"AioStreamsCredentials {{ BaseUri = {BaseUri} }}";
}
