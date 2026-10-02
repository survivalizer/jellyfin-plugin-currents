using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Jellyfin's users, sorted by name.</summary>
public sealed class JellyfinUserDirectory : IUserDirectory
{
    private readonly IUserManager _users;

    public JellyfinUserDirectory(IUserManager users) => _users = users;

    public IReadOnlyList<DirectoryUser> All() =>
        _users.GetUsers().Select(u => new DirectoryUser(u.Id, u.Username)).OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
