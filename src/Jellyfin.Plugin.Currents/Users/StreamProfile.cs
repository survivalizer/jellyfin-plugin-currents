using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Users;

/// <summary>The effective stream settings for one user (or for no user).</summary>
/// <param name="Source">The layer that supplied <paramref name="Credentials"/>.</param>
/// <param name="Credentials">The AIOStreams config to search with, if any.</param>
/// <param name="Preferences">Ranking preferences.</param>
/// <param name="AutoSelect">True to expose only the top stream.</param>
/// <param name="Disabled">True when an admin disabled streams for this user.</param>
public sealed record StreamProfile(ProfileSource Source, AioStreamsCredentials? Credentials, StreamPreferences Preferences, bool AutoSelect, bool Disabled)
{
    public bool CanPlay => !Disabled && Credentials is not null;
}
