using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Parses the resolve URL Currents writes into .strm files (…/Currents/play/{type}/{id}?sig=…).</summary>
public static class StrmUrl
{
    private const string Marker = "/Currents/play/";

    public static bool TryParse(string? value, [NotNullWhen(true)] out string? type, [NotNullWhen(true)] out string? stremioId, out string? signature)
    {
        type = null;
        stremioId = null;
        signature = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        var marker = path.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return false;
        }

        var parts = path[(marker + Marker.Length)..].Split('/');
        if (parts.Length != 2 || parts[0] is not ("movie" or "series") || parts[1].Length == 0)
        {
            return false;
        }

        type = parts[0];
        stremioId = Uri.UnescapeDataString(parts[1]);
        signature = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == "sig")
            .Select(p => Uri.UnescapeDataString(p[1]))
            .FirstOrDefault();
        return true;
    }
}
