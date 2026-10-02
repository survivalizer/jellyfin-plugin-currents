using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Names a stream by what it is (torrent file, release, URL) rather than its position, so a version keeps its id across searches.</summary>
public static class StreamIdentity
{
    /// <summary>Computes one key per result. Results that look identical get an ordinal so keys stay unique within a list.</summary>
    /// <param name="results">The results in AIOStreams order.</param>
    /// <returns>32-hex-char keys, aligned with <paramref name="results"/>.</returns>
    public static IReadOnlyList<string> Keys(IReadOnlyList<StreamResult> results)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new List<string>(results.Count);
        foreach (var result in results)
        {
            var raw = Raw(result);
            var count = seen.GetValueOrDefault(raw);
            seen[raw] = count + 1;
            keys.Add(Hash(count == 0 ? raw : $"{raw}|{count.ToString(CultureInfo.InvariantCulture)}"));
        }

        return keys;
    }

    /// <summary>The synthetic MediaSource id for one stream of one item, for one user.</summary>
    /// <param name="itemId">The base library item id.</param>
    /// <param name="userId">The Jellyfin user (<see cref="Guid.Empty"/> for no user).</param>
    /// <param name="key">The stream key from <see cref="Keys"/>.</param>
    /// <returns>A lower-case GUID in "N" format.</returns>
    public static string VersionId(Guid itemId, Guid userId, string key) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{itemId:N}|{userId:N}|{key}")).AsSpan(0, 16)).ToString("N");

    private static string Raw(StreamResult result)
    {
        var origin = $"{result.Addon}|{result.Service}";
        if (!string.IsNullOrEmpty(result.InfoHash))
        {
            var file = result.FileIdx?.ToString(CultureInfo.InvariantCulture) ?? result.Filename;
            return $"btih:{result.InfoHash.ToUpperInvariant()}|{file}|{origin}";
        }

        if (!string.IsNullOrEmpty(result.Filename))
        {
            return $"file:{result.Filename}|{result.Size?.ToString(CultureInfo.InvariantCulture)}|{origin}";
        }

        return $"url:{result.Url}";
    }

    private static string Hash(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)).AsSpan(0, 16));
}
