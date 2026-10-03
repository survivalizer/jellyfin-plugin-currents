using Jellyfin.Plugin.Currents.Streams;
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

    /// <summary>Gets or sets the base URL written into .strm files. Both clients and the Jellyfin server itself (its ffmpeg) must reach Jellyfin at it.</summary>
    public string StrmBaseUrl { get; set; } = "http://127.0.0.1:8096";

    /// <summary>Gets or sets the HMAC secret for resolve URLs (base64, generated on first start).</summary>
    public string SigningSecret { get; set; } = string.Empty;

    public CatalogSelection[] Catalogs { get; set; } = [];

    public int PruneAfterMisses { get; set; } = 3;

    public int FailoverAttempts { get; set; } = 3;

    /// <summary>Gets or sets a value indicating whether titles show per-user versions. Off is degraded mode: every title plays the default config's best stream.</summary>
    public bool EnableVersions { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users may set their own AIOStreams config and preferences on the Currents user page.</summary>
    public bool AllowSelfService { get; set; } = true;

    public StreamPreferences DefaultPreferences { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether users see only the best stream (true) or every ranked stream (false) by default.</summary>
    public bool DefaultAutoSelect { get; set; }

    public int StreamCacheMinutes { get; set; } = 60;

    public int MaxVersions { get; set; } = 20;

    public int VersionTokenHours { get; set; } = 24;

    public int AioStreamsPermitsPer10Seconds { get; set; } = 5;

    public int AioMetadataPermitsPer5Seconds { get; set; } = 15;

    /// <summary>Gets or sets a value indicating whether Jellyfin search also lists AIOMetadata titles that are not in the library yet.</summary>
    public bool EnableSearch { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users who have not chosen see (and can add) search results by default.</summary>
    public bool DefaultSearchAutoAdd { get; set; } = true;

    /// <summary>
    /// Gets or sets the AIOMetadata search catalogs to query. An array (not a list) so Jellyfin's XML deserializer
    /// replaces the defaults instead of appending to them.
    /// </summary>
    public CatalogSelection[] SearchCatalogs { get; set; } =
    [
        new CatalogSelection { Type = "movie", Id = "search.movie", Name = "Movies", Target = CatalogTarget.Movies, MaxItems = 20 },
        new CatalogSelection { Type = "series", Id = "search.series", Name = "Series", Target = CatalogTarget.Shows, MaxItems = 20 },
    ];

    /// <summary>Gets or sets a value indicating whether track lists are looked up on RemuxDB (only IMDb/TMDB ids are sent).</summary>
    public bool EnableRemuxDb { get; set; } = true;

    /// <summary>Gets or sets the RemuxDB server.</summary>
    public string RemuxDbUrl { get; set; } = "https://remuxdb.1632022.xyz";

    /// <summary>Gets or sets a value indicating whether stream-attached subtitles become tracks and the Jellyfin subtitle search queries AIOStreams.</summary>
    public bool EnableSubtitles { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether Currents fetches skip markers (intro, recap, credits, preview) for its titles.</summary>
    public bool EnableSegments { get; set; } = true;

    /// <summary>Gets or sets how far, in percent, a version's runtime may differ from the runtime its markers were made for. Read through <c>SegmentGate</c>, which clamps it to 1–10.</summary>
    public double SegmentTolerancePercent { get; set; } = 2;

    /// <summary>Gets or sets a value indicating whether a version still gets markers when its runtime, or the markers' reference runtime, is unknown.</summary>
    public bool SegmentsWhenRuntimeUnknown { get; set; }

    /// <summary>Gets or sets the optional TheIntroDB API key (raises the daily limit from 500 to 1000 lookups). Sent only as a Bearer header.</summary>
    public string TheIntroDbApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the PublicMetaDB API key. Empty turns PublicMetaDB off. Sent only as a Bearer header.</summary>
    public string PublicMetaDbApiKey { get; set; } = string.Empty;
}
