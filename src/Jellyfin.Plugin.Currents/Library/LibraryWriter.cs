using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Metadata;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Writes titles as .strm + .nfo files. Writes are atomic and idempotent.</summary>
public sealed class LibraryWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly LibraryPaths _paths;
    private readonly StrmSigner _signer;
    private readonly string _strmBaseUrl;
    private readonly TimeProvider _time;

    public LibraryWriter(LibraryPaths paths, StrmSigner signer, string strmBaseUrl, TimeProvider time)
    {
        _paths = paths;
        _signer = signer;
        _strmBaseUrl = strmBaseUrl;
        _time = time;
    }

    public WriteResult WriteMovie(TitleKey key, StremioMeta meta, string? existingRelativeFolder)
    {
        var folderName = FolderName(key, meta, existingRelativeFolder);
        var folder = Path.Combine(_paths.Movies, folderName);

        var changed = WriteIfChanged(Path.Combine(folder, "movie.nfo"), NfoWriter.Movie(key, meta));
        changed |= WriteIfChanged(Path.Combine(folder, PathNaming.MovieFile(folderName)), _signer.StrmUrl(_strmBaseUrl, "movie", key.StremioId) + "\n");
        return new WriteResult(Path.Combine("Movies", folderName), changed);
    }

    public WriteResult WriteSeries(TitleKey key, StremioMeta meta, string? existingRelativeFolder)
    {
        var folderName = FolderName(key, meta, existingRelativeFolder);
        var folder = Path.Combine(_paths.Shows, folderName);

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
        var full = Path.GetFullPath(Path.Combine(_paths.Root, relativeFolder));
        if (string.IsNullOrWhiteSpace(relativeFolder)
            || !full.StartsWith(_paths.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete a folder outside the Currents library root.");
        }

        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }
    }

    private static string FolderName(TitleKey key, StremioMeta meta, string? existingRelativeFolder) =>
        string.IsNullOrEmpty(existingRelativeFolder)
            ? PathNaming.TitleFolder(meta.Name, MetaMapper.ParseYear(meta), key)
            : Path.GetFileName(existingRelativeFolder);

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
