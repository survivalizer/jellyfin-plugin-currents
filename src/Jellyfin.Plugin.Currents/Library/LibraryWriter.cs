using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Metadata;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Writes titles as .strm + .nfo files. Writes are atomic and idempotent.</summary>
public sealed class LibraryWriter
{
    internal const string MarkerFile = ".currents";
    private static readonly string[] SubtitleExtensions = [".srt", ".vtt", ".ass", ".ssa", ".sub", ".idx", ".sup", ".smi"];
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
        var folderName = FolderName("Movies", key, meta, existingRelativeFolder);
        var folder = Path.Combine(_paths.Movies, folderName);
        EnsureManageable(folder);

        WriteIfChanged(Path.Combine(folder, MarkerFile), string.Empty);
        var changed = WriteIfChanged(Path.Combine(folder, "movie.nfo"), NfoWriter.Movie(key, meta));
        changed |= WriteIfChanged(Path.Combine(folder, PathNaming.MovieFile(folderName)), _signer.StrmUrl(_strmBaseUrl, "movie", key.StremioId) + "\n");
        return new WriteResult(Path.Combine("Movies", folderName), changed);
    }

    public WriteResult WriteSeries(TitleKey key, StremioMeta meta, string? existingRelativeFolder)
    {
        var folderName = FolderName("Shows", key, meta, existingRelativeFolder);
        var folder = Path.Combine(_paths.Shows, folderName);
        EnsureManageable(folder);

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

        if (!File.Exists(Path.Combine(full, MarkerFile)))
        {
            _logger?.LogInformation("Left {Folder} untouched because it has no {Marker} marker, so Currents does not manage it", relativeFolder, MarkerFile);
            return;
        }

        // Only plugin-owned files are removed; anything else in the folder is user or Jellyfin content and stays.
        foreach (var sub in Directory.GetDirectories(full).Where(IsSeasonFolder))
        {
            var names = StrmNames(sub);
            foreach (var file in Directory.GetFiles(sub).Where(f => IsStrm(f) || IsCompanionSubtitle(f, names)))
            {
                File.Delete(file);
            }

            RemoveIfEmpty(sub);
        }

        var topNames = StrmNames(full);
        foreach (var file in Directory.GetFiles(full).Where(f => IsPluginFile(f, topNames)))
        {
            File.Delete(file);
        }

        // The marker goes last, and only when nothing else is left, so a kept folder stays recognisably ours.
        var marker = Path.Combine(full, MarkerFile);
        if (Directory.EnumerateFileSystemEntries(full).All(e => string.Equals(e, marker, StringComparison.Ordinal)))
        {
            File.Delete(marker);
            Directory.Delete(full);
        }
        else
        {
            _logger?.LogInformation("Left {Folder} in place because it contains files Currents did not create", relativeFolder);
        }
    }

    private static bool IsSeasonFolder(string path)
    {
        var name = Path.GetFileName(path);
        return name == "Specials" || (name.StartsWith("Season ", StringComparison.Ordinal) && name.Length > 7 && name[7..].All(char.IsAsciiDigit));
    }

    private static bool IsStrm(string path) => path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase);

    private static bool IsPluginContentFile(string path) =>
        IsStrm(path) || Path.GetFileName(path) is "movie.nfo" or "tvshow.nfo";

    private static string[] StrmNames(string folder) =>
        Directory.GetFiles(folder, "*.strm").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray();

    // A subtitle Jellyfin saved for one of the folder's .strm files: "{strm name}.{lang}[.forced][.sdh][.N].{ext}".
    private static bool IsCompanionSubtitle(string path, IReadOnlyCollection<string> strmNames) =>
        SubtitleExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        && strmNames.Any(name => Path.GetFileName(path).StartsWith(name + ".", StringComparison.Ordinal));

    private static bool IsPluginFile(string path, IReadOnlyCollection<string> strmNames) =>
        IsPluginContentFile(path) || IsCompanionSubtitle(path, strmNames);

    private static void RemoveIfEmpty(string directory)
    {
        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>
    /// A folder may be written when it does not exist, carries the marker, or holds nothing but files Currents
    /// writes itself (then it is adopted and the marker is added). Anything else is user content.
    /// </summary>
    private static void EnsureManageable(string folder)
    {
        if (!Directory.Exists(folder) || File.Exists(Path.Combine(folder, MarkerFile)) || HoldsOnlyPluginFiles(folder))
        {
            return;
        }

        throw new InvalidOperationException($"The target folder {Path.GetFileName(folder)} exists and is not managed by Currents.");
    }

    private static bool HoldsOnlyPluginFiles(string folder)
    {
        var names = StrmNames(folder);
        return Directory.GetFiles(folder).All(f => IsPluginFile(f, names))
            && Directory.GetDirectories(folder).All(sub =>
            {
                var episodes = StrmNames(sub);
                return IsSeasonFolder(sub)
                    && Directory.GetDirectories(sub).Length == 0
                    && Directory.GetFiles(sub).All(f => IsStrm(f) || IsCompanionSubtitle(f, episodes));
            });
    }

    private static bool IsBelow(string full, string parent) =>
        full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string FolderName(string category, TitleKey key, StremioMeta meta, string? existingRelativeFolder)
    {
        if (existingRelativeFolder is not null)
        {
            var parts = existingRelativeFolder.Replace('\\', '/').Split('/');
            if (parts.Length == 2
                && string.Equals(parts[0], category, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(parts[1])
                && parts[1] is not "." and not "..")
            {
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
