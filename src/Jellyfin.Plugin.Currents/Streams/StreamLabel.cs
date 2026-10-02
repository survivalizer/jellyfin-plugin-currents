using System.Globalization;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Builds the one-line name a client shows in its Version dropdown.</summary>
public static class StreamLabel
{
    public static string For(StreamResult result)
    {
        var parsed = result.ParsedFile;
        var parts = new List<string>();

        var resolution = Known(parsed?.Resolution);
        var hdr = parsed?.VisualTags?.Where(VisualTags.IsHdr).ToList() ?? [];
        var picture = string.Join(' ', new[] { resolution, hdr.Count > 0 ? string.Join('/', hdr) : null }.Where(p => p is not null));
        Add(parts, picture.Length > 0 ? picture : null);
        Add(parts, Known(parsed?.Encode));
        Add(parts, parsed?.AudioTags?.Select(Known).FirstOrDefault(t => t is not null));
        if (result.Size is > 0)
        {
            parts.Add(FormatSize(result.Size.Value));
        }

        if (result.Cached is { } cached)
        {
            parts.Add(cached ? "cached" : "uncached");
        }

        Add(parts, string.IsNullOrWhiteSpace(result.Addon) ? null : result.Addon.Trim());
        if (parts.Count > 0)
        {
            return string.Join(" · ", parts);
        }

        return string.IsNullOrWhiteSpace(result.Filename) ? "Stream" : result.Filename.Trim();
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1_000_000_000
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_000_000_000d:0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, bytes / 1_000_000)} MB");

    private static string? Known(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();

    private static void Add(List<string> parts, string? part)
    {
        if (part is not null)
        {
            parts.Add(part);
        }
    }
}
