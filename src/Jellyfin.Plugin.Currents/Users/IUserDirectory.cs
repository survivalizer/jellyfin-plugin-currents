namespace Jellyfin.Plugin.Currents.Users;

/// <summary>Lists Jellyfin users (indirection over IUserManager, which only Integration/ may touch).</summary>
public interface IUserDirectory
{
    IReadOnlyList<DirectoryUser> All();
}
