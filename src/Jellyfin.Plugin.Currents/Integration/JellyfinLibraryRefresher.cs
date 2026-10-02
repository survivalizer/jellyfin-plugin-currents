using Jellyfin.Plugin.Currents.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Refreshes only the Currents folders instead of the whole library.</summary>
public sealed class JellyfinLibraryRefresher : ILibraryRefresher
{
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<JellyfinLibraryRefresher> _logger;
    private int _scanQueued;

    public JellyfinLibraryRefresher(ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<JellyfinLibraryRefresher> logger)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task RefreshAsync(IReadOnlyCollection<string> folders, CancellationToken cancellationToken)
    {
        foreach (var path in folders.Where(Directory.Exists))
        {
            if (_libraryManager.FindByPath(path, isFolder: true) is Folder folder)
            {
                await folder.ValidateChildren(
                    new Progress<double>(),
                    new MetadataRefreshOptions(new DirectoryService(_fileSystem)),
                    recursive: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else if (Interlocked.Exchange(ref _scanQueued, 1) == 0)
            {
                // A library created without a scan has no folder item for its root yet; a full scan creates it.
                // Queued at most once per process (this class is a singleton) so an unused folder cannot cause
                // a full library scan after every sync.
                _libraryManager.QueueLibraryScan();
                _logger.LogInformation(
                    "Currents folder {Path} has not been scanned into a Jellyfin library yet; queued a library scan. If it is not part of any library, add it to a Movies or Shows library.",
                    path);
            }
            else
            {
                _logger.LogInformation(
                    "Currents folder {Path} is not part of any Jellyfin library yet; add it to a Movies or Shows library to see its titles.",
                    path);
            }
        }
    }
}
