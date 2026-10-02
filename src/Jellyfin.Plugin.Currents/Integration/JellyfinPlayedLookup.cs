using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Checks Jellyfin user data so pruning never removes something a user watched or started.</summary>
public sealed class JellyfinPlayedLookup : IPlayedLookup
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;

    public JellyfinPlayedLookup(ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
    }

    public bool IsPlayedByAnyone(string absoluteFolder, MediaKind kind)
    {
        var items = ItemsUnder(absoluteFolder, kind);
        if (items.Count == 0)
        {
            return false;
        }

        foreach (var user in _userManager.GetUsers())
        {
            foreach (var item in items)
            {
                var data = _userDataManager.GetUserData(user, item);
                if (data is not null && (data.Played || data.PlaybackPositionTicks > 0))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IReadOnlyList<BaseItem> ItemsUnder(string folder, MediaKind kind)
    {
        if (kind == MediaKind.Movie)
        {
            var strm = Path.Combine(folder, PathNaming.MovieFile(Path.GetFileName(folder)));
            return _libraryManager.FindByPath(strm, isFolder: false) is { } movie ? new[] { movie } : Array.Empty<BaseItem>();
        }

        if (_libraryManager.FindByPath(folder, isFolder: true) is not { } series)
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = [series.Id],
            IncludeItemTypes = [BaseItemKind.Episode],
        });
    }
}
