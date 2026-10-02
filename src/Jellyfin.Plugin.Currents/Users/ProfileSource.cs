namespace Jellyfin.Plugin.Currents.Users;

/// <summary>Which layer supplied a user's AIOStreams config.</summary>
public enum ProfileSource
{
    /// <summary>No layer has a config: titles show a single "Streams not configured" version.</summary>
    None,

    /// <summary>The server default config.</summary>
    Default,

    /// <summary>An admin per-user override.</summary>
    Admin,

    /// <summary>The user's own config from the self-service page.</summary>
    Self,
}
