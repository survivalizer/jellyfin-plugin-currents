namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Request body carrying a manifest URL, kept out of query strings because it contains credentials.</summary>
public sealed record ManifestUrlRequest(string ManifestUrl);
