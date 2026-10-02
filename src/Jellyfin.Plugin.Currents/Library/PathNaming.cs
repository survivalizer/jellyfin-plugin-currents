using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>File and folder names in Jellyfin's naming convention, safe on Windows, macOS and Linux.</summary>
public static partial class PathNaming
{
    private const int MaxTitleLength = 100;
    private const string Fallback = "Untitled";
    private static readonly SearchValues<char> Invalid = SearchValues.Create("<>:\"/\\|?*");

    public static string SanitizeTitle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            builder.Append(char.IsControl(c) || Invalid.Contains(c) ? ' ' : c);
        }

        var cleaned = Whitespace().Replace(builder.ToString(), " ").Trim().TrimEnd('.', ' ');
        if (cleaned.Length > MaxTitleLength)
        {
            var cut = char.IsHighSurrogate(cleaned[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
            cleaned = cleaned[..cut].TrimEnd('.', ' ');
        }

        return cleaned.Length == 0 || cleaned.All(c => !char.IsLetterOrDigit(c)) ? Fallback : cleaned;
    }

    public static string TitleFolder(string? name, int? year, TitleKey key) =>
        year is int y
            ? string.Create(CultureInfo.InvariantCulture, $"{SanitizeTitle(name)} ({y}) {key.FolderTag}")
            : $"{SanitizeTitle(name)} {key.FolderTag}";

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
