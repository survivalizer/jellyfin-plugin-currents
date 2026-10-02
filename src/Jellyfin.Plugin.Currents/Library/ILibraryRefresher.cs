namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Asks Jellyfin to rescan specific folders.</summary>
public interface ILibraryRefresher
{
    Task RefreshAsync(IReadOnlyCollection<string> folders, CancellationToken cancellationToken);
}
