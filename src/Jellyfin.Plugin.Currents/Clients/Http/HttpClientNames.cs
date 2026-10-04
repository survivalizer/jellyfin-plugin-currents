namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Names of the IHttpClientFactory clients Currents registers.</summary>
public static class HttpClientNames
{
    public const string AioStreams = "Currents.AioStreams";
    public const string AioMetadata = "Currents.AioMetadata";
    public const string Resolve = "Currents.Resolve";
    public const string Posters = "Currents.Posters";
    public const string RemuxDb = "Currents.RemuxDb";
    public const string Subtitles = "Currents.Subtitles";
    public const string Proxy = "Currents.Proxy";
    public const string Segments = "Currents.Segments";

    /// <summary>Library artwork (posters, backdrops, logos) Jellyfin's metadata refresh downloads through the AIOMetadata providers. Guarded like Posters.</summary>
    public const string Artwork = "Currents.Artwork";
}
