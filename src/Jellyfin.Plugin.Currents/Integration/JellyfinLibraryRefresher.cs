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
                _logger.LogWarning("Currents folder {Path} is not in any Jellyfin library yet. Add it to a Movies or Shows library.", path);
            }
        }
    }
}
