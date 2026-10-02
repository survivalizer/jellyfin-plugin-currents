using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Users;

/// <summary>One layer of a user's settings (admin override or self-service). Null means "not set here; ask the next layer".</summary>
public sealed class UserLayer
{
    /// <summary>Gets or sets the AIOStreams manifest URL. It holds the config password, so the API never returns it.</summary>
    public string? AioStreamsManifestUrl { get; set; }

    public StreamPreferences? Preferences { get; set; }

    public bool? AutoSelect { get; set; }

    public bool? SearchAutoAdd { get; set; }
}
