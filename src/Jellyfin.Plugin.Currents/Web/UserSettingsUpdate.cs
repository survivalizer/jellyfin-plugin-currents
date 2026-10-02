using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>A self-service change. Null fields keep the saved value.</summary>
public sealed class UserSettingsUpdate
{
    /// <summary>Gets or sets the AIOStreams manifest URL: null keeps, empty removes, anything else is validated and saved.</summary>
    public string? AioStreamsManifestUrl { get; set; }

    public StreamPreferences? Preferences { get; set; }

    public bool ClearPreferences { get; set; }

    public bool? AutoSelect { get; set; }

    public bool ClearAutoSelect { get; set; }
}
