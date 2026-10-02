namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A user's (or the server default's) stream filters and ranking preferences (spec §5.4).</summary>
public sealed class StreamPreferences
{
    /// <summary>Gets or sets a value indicating whether uncached debrid streams are hidden (streams without a cache state are kept).</summary>
    public bool CachedOnly { get; set; }

    /// <summary>Gets or sets the largest stream size in GB (decimal). 0 means no limit.</summary>
    public double MaxSizeGb { get; set; }

    /// <summary>Gets or sets resolutions that are hidden.</summary>
    public string[] ExcludedResolutions { get; set; } = [];

    /// <summary>Gets or sets visual tags that hide a stream.</summary>
    public string[] ExcludedVisualTags { get; set; } = [];

    /// <summary>Gets or sets resolutions in preferred order (e.g. 2160p, 1080p). Unlisted resolutions rank after listed ones.</summary>
    public string[] ResolutionOrder { get; set; } = [];

    /// <summary>Gets or sets how HDR streams are ranked.</summary>
    public HdrPreference Hdr { get; set; } = HdrPreference.Any;

    /// <summary>Gets or sets audio languages in preferred order, as AIOStreams names them (e.g. English, Japanese).</summary>
    public string[] AudioLanguages { get; set; } = [];

    /// <summary>Gets or sets subtitle languages in preferred order.</summary>
    public string[] SubtitleLanguages { get; set; } = [];

    /// <summary>Replaces null lists (from hand-edited or partial JSON/XML) with empty ones.</summary>
    public void Normalize()
    {
#pragma warning disable CS8601, IDE0074 // Untrusted input can put nulls in non-nullable properties.
        ExcludedResolutions ??= [];
        ExcludedVisualTags ??= [];
        ResolutionOrder ??= [];
        AudioLanguages ??= [];
        SubtitleLanguages ??= [];
#pragma warning restore CS8601, IDE0074
        if (double.IsNaN(MaxSizeGb) || MaxSizeGb < 0)
        {
            MaxSizeGb = 0;
        }
    }
}
