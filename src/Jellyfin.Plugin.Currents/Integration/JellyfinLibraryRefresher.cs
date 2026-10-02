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

    public JellyfinLibraryRefresher(ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<JellyfinLibraryRefresher> logger)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task RefreshAsync(IReadOnlyCollection<string> folders, CancellationToken cancellationToken)
    {
        var scanQueued = false;
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
            else
            {
                // A library created without a scan has no folder item for its root yet; a full scan creates it.
                if (!scanQueued)
                {
                    _libraryManager.QueueLibraryScan();
                    scanQueued = true;
                }

                _logger.LogInformation(
                    "Currents folder {Path} has not been scanned into a Jellyfin library yet; queued a library scan. If it is not part of any library, add it to a Movies or Shows library.",
                    path);
            }
        }
    }
}
