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

        if (int.TryParse(runtime.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bare))
        {
            return TimeSpan.FromMinutes(bare).Ticks;
        }

        var hours = Hours().Match(runtime);
        var minutes = Minutes().Match(runtime);
        if (!hours.Success && !minutes.Success)
        {
            return null;
        }

        var total = (hours.Success ? int.Parse(hours.Groups[1].Value, CultureInfo.InvariantCulture) * 60 : 0)
            + (minutes.Success ? int.Parse(minutes.Groups[1].Value, CultureInfo.InvariantCulture) : 0);
        return TimeSpan.FromMinutes(total).Ticks;
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
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"\b(18|19|20)\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Year();

    [GeneratedRegex(@"(\d+)\s*h", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Hours();

    [GeneratedRegex(@"(\d+)\s*m", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Minutes();
}
