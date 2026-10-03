using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A title's RemuxDB versions by torrent info hash, and the rule that ties one stream to one of them.</summary>
public sealed class RemuxDbIndex
{
    private readonly Dictionary<string, List<(RemuxDbVersion Version, RemuxDbSource Source)>> _byHash;

    private RemuxDbIndex(Dictionary<string, List<(RemuxDbVersion Version, RemuxDbSource Source)>> byHash) => _byHash = byHash;

    public static RemuxDbIndex Empty { get; } = new(new Dictionary<string, List<(RemuxDbVersion, RemuxDbSource)>>(StringComparer.OrdinalIgnoreCase));

    public static RemuxDbIndex Create(IEnumerable<RemuxDbVersion> versions)
    {
        // Info hashes are hex; RemuxDB and AIOStreams differ in case.
        var byHash = new Dictionary<string, List<(RemuxDbVersion, RemuxDbSource)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in versions)
        {
            foreach (var source in version.Sources ?? [])
            {
                if (source.Kind == "torrent" && !string.IsNullOrWhiteSpace(source.TorrentInfoHash))
                {
                    var key = source.TorrentInfoHash.Trim();
                    if (!byHash.TryGetValue(key, out var list))
                    {
                        byHash[key] = list = [];
                    }

                    list.Add((version, source));
                }
            }
        }

        return new RemuxDbIndex(byHash);
    }

    /// <summary>
    /// Same info hash, and then: the same file index or file name, or, when none matches exactly, the size fallback: only one
    /// plausible candidate left (candidates naming a different file are already excluded) whose size is within 1 % of the stream's size.
    /// A candidate naming a different file never matches, and two matches are no match.
    /// </summary>
    /// <param name="result">The stream.</param>
    /// <returns>The matching version, or null.</returns>
    public RemuxDbVersion? Match(StreamResult result)
    {
        if (string.IsNullOrWhiteSpace(result.InfoHash) || !_byHash.TryGetValue(result.InfoHash.Trim(), out var candidates))
        {
            return null;
        }

        var name = FileName(result.Filename);
        var plausible = candidates
            .Where(c => !(result.FileIdx is { } wanted && c.Source.TorrentFileIdx is { } index && index != wanted))
            .Where(c => !(name is not null && FileName(c.Source.Filename) is { } other && !string.Equals(other, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var exact = plausible
            .Where(c => (result.FileIdx is { } wanted && c.Source.TorrentFileIdx == wanted)
                || (name is not null && string.Equals(FileName(c.Source.Filename), name, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Version)
            .Distinct()
            .ToList();
        if (exact.Count > 0)
        {
            return exact.Count == 1 ? exact[0] : null;
        }

        var rest = plausible.Select(c => c.Version).Distinct().ToList();
        return rest.Count == 1 && rest[0].Size is > 0 and var size && result.Size is > 0 and var wantedSize && Math.Abs(size - wantedSize) <= wantedSize / 100
            ? rest[0]
            : null;
    }

    private static string? FileName(string? value)
    {
        var name = value is null ? null : Path.GetFileName(value.Replace('\\', '/').TrimEnd('/'));
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }
}
