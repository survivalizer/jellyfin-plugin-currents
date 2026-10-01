using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Currents.Configuration;

/// <summary>Global Currents settings, stored by Jellyfin as XML.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the AIOMetadata manifest URL (https://host/stremio/{uuid}/manifest.json).</summary>
    public string AioMetadataManifestUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the default AIOStreams manifest URL (https://host/stremio/{uuid}/{password}/manifest.json).</summary>
    public string AioStreamsManifestUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the folder that holds Movies/ and Shows/. Empty means the plugin data folder.</summary>
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>Gets or sets the base URL written into .strm files. Jellyfin must be reachable at it.</summary>
    public string StrmBaseUrl { get; set; } = "http://127.0.0.1:8096";

    /// <summary>Gets or sets the HMAC secret for resolve URLs (base64, generated on first start).</summary>
    public string SigningSecret { get; set; } = string.Empty;

    public CatalogSelection[] Catalogs { get; set; } = [];

    public int PruneAfterMisses { get; set; } = 3;

    public int FailoverAttempts { get; set; } = 3;

    public int AioStreamsPermitsPer10Seconds { get; set; } = 5;

    public int AioMetadataPermitsPer5Seconds { get; set; } = 15;
}
