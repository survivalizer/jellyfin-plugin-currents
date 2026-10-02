namespace Jellyfin.Plugin.Currents.Users;

/// <summary>A Jellyfin user as the admin page lists them.</summary>
public sealed record DirectoryUser(Guid Id, string Name);
