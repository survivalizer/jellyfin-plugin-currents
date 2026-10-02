using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Metadata;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Writes titles as .strm + .nfo files. Writes are atomic and idempotent.</summary>
public sealed class LibraryWriter
{
    private const string MarkerFile = ".currents";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly LibraryPaths _paths;
    private readonly StrmSigner _signer;
    private readonly string _strmBaseUrl;
    private readonly TimeProvider _time;
    private readonly ILogger? _logger;

    public LibraryWriter(LibraryPaths paths, StrmSigner signer, string strmBaseUrl, TimeProvider time, ILogger? logger = null)
    {
        _paths = paths;
        _signer = signer;
        _strmBaseUrl = strmBaseUrl;
        _time = time;
        _logger = logger;
    }

    public WriteResult WriteMovie(TitleKey key, StremioMeta meta, string? existingRelativeFolder)
    {
        var folderName = FolderName("Movies", key, meta, existingRelativeFolder, out var reused);
        var folder = Path.Combine(_paths.Movies, folderName);
        EnsureManageable(folder, reused);

        WriteIfChanged(Path.Combine(folder, MarkerFile), string.Empty);
        var changed = WriteIfChanged(Path.Combine(folder, "movie.nfo"), NfoWriter.Movie(key, meta));
        changed |= WriteIfChanged(Path.Combine(folder, PathNaming.MovieFile(folderName)), _signer.StrmUrl(_strmBaseUrl, "movie", key.StremioId) + "\n");
        return new WriteResult(Path.Combine("Movies", folderName), changed);
    }

    public WriteResult WriteSeries(TitleKey key, StremioMeta meta, string? existingRelativeFolder)
    {
        var folderName = FolderName("Shows", key, meta, existingRelativeFolder, out var reused);
        var folder = Path.Combine(_paths.Shows, folderName);
        EnsureManageable(folder, reused);

        WriteIfChanged(Path.Combine(folder, MarkerFile), string.Empty);
        var changed = WriteIfChanged(Path.Combine(folder, "tvshow.nfo"), NfoWriter.TvShow(key, meta));
        foreach (var (season, episode, episodeId) in ReleasedEpisodes(key, meta))
        {
            var path = Path.Combine(folder, PathNaming.SeasonFolder(season), PathNaming.EpisodeFile(folderName, season, episode));
            changed |= WriteIfChanged(path, _signer.StrmUrl(_strmBaseUrl, "series", episodeId) + "\n");
        }

        return new WriteResult(Path.Combine("Shows", folderName), changed);
    }

    public void Delete(string relativeFolder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(_paths.Root, relativeFolder)));
        if (string.IsNullOrWhiteSpace(relativeFolder)
            || !(IsBelow(full, _paths.Movies) || IsBelow(full, _paths.Shows)))
        {
            throw new InvalidOperationException("Refusing to delete a folder outside the Currents title folders.");
        }

        if (!Directory.Exists(full))
        {
            return;
        }

        // Only plugin-owned files are removed; anything else in the folder is user content and stays.
        foreach (var sub in Directory.GetDirectories(full).Where(IsSeasonFolder))
        {
            foreach (var strm in Directory.GetFiles(sub, "*.strm"))
            {
                File.Delete(strm);
            }

            RemoveIfEmpty(sub);
        }

        foreach (var file in Directory.GetFiles(full).Where(IsPluginFile))
        {
            File.Delete(file);
        }

        if (!RemoveIfEmpty(full))
        {
            _logger?.LogInformation("Left {Folder} in place because it contains files Currents did not create", relativeFolder);
        }
    }

    private static bool IsSeasonFolder(string path)
    {
        var name = Path.GetFileName(path);
        return name == "Specials" || (name.StartsWith("Season ", StringComparison.Ordinal) && name.Length > 7 && name[7..].All(char.IsAsciiDigit));
    }

    private static bool IsPluginFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
            || name is "movie.nfo" or "tvshow.nfo" or MarkerFile;
    }

    private static bool RemoveIfEmpty(string directory)
    {
        if (Directory.EnumerateFileSystemEntries(directory).Any())
        {
            return false;
        }

        Directory.Delete(directory);
        return true;
    }

    private static void EnsureManageable(string folder, bool reused)
    {
        if (!reused && Directory.Exists(folder) && !File.Exists(Path.Combine(folder, MarkerFile)))
        {
            throw new InvalidOperationException("The target folder exists and is not managed by Currents.");
        }
    }

    private static bool IsBelow(string full, string parent) =>
        full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string FolderName(string category, TitleKey key, StremioMeta meta, string? existingRelativeFolder, out bool reused)
    {
        reused = false;
        if (existingRelativeFolder is not null)
        {
            var parts = existingRelativeFolder.Replace('\\', '/').Split('/');
            if (parts.Length == 2
                && string.Equals(parts[0], category, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(parts[1])
                && parts[1] is not "." and not "..")
            {
                reused = true;
                return parts[1];
            }
        }

        return PathNaming.TitleFolder(meta.Name, MetaMapper.ParseYear(meta), key);
    }

    private static bool WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path, Utf8NoBom), content, StringComparison.Ordinal))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, Utf8NoBom);
        File.Move(temp, path, overwrite: true);
        return true;
    }

    private IEnumerable<(int Season, int Episode, string EpisodeId)> ReleasedEpisodes(TitleKey key, StremioMeta meta)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return (meta.Videos ?? [])
            .Where(v => v.Season is >= 0 && v.Episode is > 0)
            .Where(v => MetaMapper.ParseDate(v.Released) is not DateTime released || released <= now)
            .Select(v => (Season: v.Season!.Value, Episode: v.Episode!.Value, EpisodeId: string.IsNullOrWhiteSpace(v.Id) ? key.EpisodeId(v.Season!.Value, v.Episode!.Value) : v.Id))
            .DistinctBy(e => (e.Season, e.Episode));
    }
}
