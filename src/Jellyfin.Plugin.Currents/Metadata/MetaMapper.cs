using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Converts AIOMetadata meta fields into Jellyfin values.</summary>
public static partial class MetaMapper
{
    public static int? ParseYear(StremioMeta meta) =>
        FirstYear(meta.Year) ?? FirstYear(meta.ReleaseInfo) ?? ParseDate(meta.Released)?.Year;

    public static DateTime? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date.UtcDateTime
            : null;

    public static long? ParseRuntimeTicks(string? runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime))
        {
            return null;
        }

        if (int.TryParse(runtime.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var bare))
        {
            return InRange(bare);
        }

        var hours = Hours().Match(runtime);
        var minutes = Minutes().Match(runtime);
        if (!hours.Success && !minutes.Success)
        {
            return null;
        }

        var h = 0;
        var m = 0;
        if ((hours.Success && !int.TryParse(hours.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out h))
            || (minutes.Success && !int.TryParse(minutes.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out m)))
        {
            return null;
        }

        return InRange((h * 60) + m);
    }

    public static float? ParseRating(string? value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) ? rating : null;

    private static int? FirstYear(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = Year().Match(value);
        return match.Success && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;
    }

    private static long? InRange(int minutes) =>
        minutes is >= 1 and <= 10000 ? TimeSpan.FromMinutes(minutes).Ticks : null;

    [GeneratedRegex(@"(?<![0-9])(18|19|20)[0-9]{2}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex Year();

    [GeneratedRegex(@"(?<![0-9])([0-9]{1,5})\s*h", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Hours();

    [GeneratedRegex(@"(?<![0-9])([0-9]{1,5})\s*m", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Minutes();
}
