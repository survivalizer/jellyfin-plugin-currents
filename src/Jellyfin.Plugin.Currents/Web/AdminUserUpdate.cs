using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>An admin change to one user. Null override fields keep the saved value; the flags are always set.</summary>
public sealed class AdminUserUpdate
{
    public string? AioStreamsManifestUrl { get; set; }

    public StreamPreferences? Preferences { get; set; }

    public bool ClearPreferences { get; set; }

    public bool? AutoSelect { get; set; }

    public bool ClearAutoSelect { get; set; }

    public bool LockSelfService { get; set; }

    public bool StreamsDisabled { get; set; }

    public bool SearchAutoAddDisabled { get; set; }
}
