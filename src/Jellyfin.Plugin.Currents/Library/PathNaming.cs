using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>File and folder names in Jellyfin's naming convention, safe on Windows, macOS and Linux.</summary>
public static partial class PathNaming
{
    private const int MaxTitleLength = 100;
    private const int MaxTitleBytes = 150;
    private const string Fallback = "Untitled";
    private static readonly SearchValues<char> Invalid = SearchValues.Create("<>:\"/\\|?*");

    public static string SanitizeTitle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsControl(c) || Invalid.Contains(c) ? ' ' : c);
        }

        // Unpaired surrogates are replaced by a space (so "A\uD800B" becomes "A B") because Normalize would throw.
        var text = builder.ToString();
        builder.Clear();
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text[i]).Append(text[i + 1]);
                i++;
            }
            else
            {
                builder.Append(char.IsSurrogate(text[i]) ? ' ' : text[i]);
            }
        }

        var cleaned = Whitespace().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim().TrimEnd('.', ' ');
        if (cleaned.Length > MaxTitleLength)
        {
            var cut = char.IsHighSurrogate(cleaned[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
            cleaned = cleaned[..cut].TrimEnd('.', ' ');
        }

        if (Encoding.UTF8.GetByteCount(cleaned) > MaxTitleBytes)
        {
            var bytes = 0;
            var end = 0;
            while (end < cleaned.Length)
            {
                var step = char.IsHighSurrogate(cleaned[end]) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(cleaned.AsSpan(end, step));
                if (bytes + size > MaxTitleBytes)
                {
                    break;
                }

                bytes += size;
                end += step;
            }

            cleaned = cleaned[..end].TrimEnd('.', ' ');
        }

        return cleaned.Length == 0 || cleaned.All(c => !char.IsLetterOrDigit(c)) ? Fallback : cleaned;
    }

    public static string TitleFolder(string? name, int? year, TitleKey key)
    {
        var title = SanitizeTitle(name);
        if (year is not int y)
        {
            return $"{title} {key.FolderTag}";
        }

        var suffix = string.Create(CultureInfo.InvariantCulture, $"({y})");
        return title.EndsWith(suffix, StringComparison.Ordinal)
            ? $"{title} {key.FolderTag}"
            : $"{title} {suffix} {key.FolderTag}";
    }

    public static string StripTag(string folderName) => TrailingTag().Replace(folderName, string.Empty);

    public static string MovieFile(string folderName) => StripTag(folderName) + ".strm";

    public static string SeasonFolder(int season) =>
        season == 0 ? "Specials" : string.Create(CultureInfo.InvariantCulture, $"Season {season:00}");

    public static string EpisodeFile(string folderName, int season, int episode) =>
        string.Create(CultureInfo.InvariantCulture, $"{StripTag(folderName)} S{season:00}E{episode:00}.strm");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*\[[^\]]+\]$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingTag();
}
