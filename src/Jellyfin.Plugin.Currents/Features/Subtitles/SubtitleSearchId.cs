using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Features.Subtitles;

/// <summary>Opaque ids for subtitle search results: the title and the subtitle's key, never its URL.</summary>
public static partial class SubtitleSearchId
{
    public static string Encode(CurrentsTitle title, string subtitleKey) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{title.Type}|{title.StremioId}|{subtitleKey}"));

    public static bool TryDecode(string? id, [NotNullWhen(true)] out CurrentsTitle? title, [NotNullWhen(true)] out string? subtitleKey)
    {
        (title, subtitleKey) = (null, null);
        if (string.IsNullOrEmpty(id) || id.Length > 512)
        {
            return false;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(id));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = text.Split('|');
        if (parts.Length != 3 || parts[0] is not ("movie" or "series") || parts[1].Length == 0 || !Key().IsMatch(parts[2]))
        {
            return false;
        }

        (title, subtitleKey) = (new CurrentsTitle(parts[0], parts[1]), parts[2]);
        return true;
    }

    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex Key();
}
