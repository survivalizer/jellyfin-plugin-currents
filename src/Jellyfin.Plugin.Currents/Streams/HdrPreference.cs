namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>How HDR / Dolby Vision streams are ranked.</summary>
public enum HdrPreference
{
    /// <summary>No preference; AIOStreams' order decides.</summary>
    Any,

    /// <summary>HDR and Dolby Vision streams first.</summary>
    Prefer,

    /// <summary>SDR streams first (for screens that cannot show HDR).</summary>
    Avoid,
}
