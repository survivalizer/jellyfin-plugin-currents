using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Jellyfin.Plugin.Currents.Clients.AioMetadata;

/// <summary>An AIOMetadata configuration, addressed by its manifest URL.</summary>
/// <param name="BaseUri">The configuration's base URI, ending in a slash.</param>
public sealed record AioMetadataEndpoint(Uri BaseUri)
{
    private const string ManifestFile = "manifest.json";

    /// <summary>Gets the manifest URI.</summary>
    public Uri Manifest => new(BaseUri, ManifestFile);

    /// <summary>Parses an AIOMetadata manifest URL.</summary>
    /// <param name="manifestUrl">The manifest URL pasted by the admin.</param>
    /// <param name="endpoint">The parsed endpoint when successful.</param>
    /// <param name="error">A user-facing message when parsing fails.</param>
    /// <returns>True when the URL is a valid manifest URL.</returns>
    public static bool TryParse(string? manifestUrl, [NotNullWhen(true)] out AioMetadataEndpoint? endpoint, out string? error)
    {
        endpoint = null;
        error = null;
        if (!Uri.TryCreate(manifestUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Enter the full http(s) AIOMetadata manifest URL.";
            return false;
        }

        if (!uri.AbsolutePath.EndsWith("/" + ManifestFile, StringComparison.OrdinalIgnoreCase))
        {
            error = "The AIOMetadata URL must end with /manifest.json.";
            return false;
        }

        var basePath = uri.AbsolutePath[..^ManifestFile.Length];
        endpoint = new AioMetadataEndpoint(new UriBuilder(uri) { Path = basePath, Query = string.Empty, Fragment = string.Empty }.Uri);
        return true;
    }

    /// <summary>Builds a catalog URI.</summary>
    /// <param name="type">The Stremio type.</param>
    /// <param name="id">The catalog id.</param>
    /// <param name="skip">Items to skip; zero or less omits the extra.</param>
    /// <returns>The catalog URI.</returns>
    public Uri Catalog(string type, string id, int skip)
    {
        var relative = skip <= 0
            ? $"catalog/{Escape(type)}/{Escape(id)}.json"
            : string.Create(CultureInfo.InvariantCulture, $"catalog/{Escape(type)}/{Escape(id)}/skip={skip}.json");
        return new Uri(BaseUri, relative);
    }

    /// <summary>Builds a meta URI.</summary>
    /// <param name="type">The Stremio type.</param>
    /// <param name="id">The item id.</param>
    /// <returns>The meta URI.</returns>
    public Uri Meta(string type, string id) => new(BaseUri, $"meta/{Escape(type)}/{Escape(id)}.json");

    /// <summary>Builds a search URI. The query is escaped into the single extras segment AIOMetadata parses.</summary>
    /// <param name="type">The Stremio type.</param>
    /// <param name="id">The search catalog id.</param>
    /// <param name="query">The search text.</param>
    /// <returns>The search URI.</returns>
    public Uri Search(string type, string id, string query) =>
        new(BaseUri, $"catalog/{Escape(type)}/{Escape(id)}/search={Escape(query)}.json");

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
