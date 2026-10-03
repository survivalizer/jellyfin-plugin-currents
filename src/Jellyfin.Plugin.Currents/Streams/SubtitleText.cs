using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Recognises SRT, WebVTT and ASS/SSA text and converts it to SRT, the one format the loopback subtitle route serves.</summary>
public static partial class SubtitleText
{
    public static string? Sniff(string text)
    {
        var head = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (head.StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            return "vtt";
        }

        if (head.StartsWith("[Script Info]", StringComparison.OrdinalIgnoreCase))
        {
            return head.Contains("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) ? "ass" : "ssa";
        }

        return SrtCue().IsMatch(head.Length > 4096 ? head[..4096] : head) ? "srt" : null;
    }

    public static string? ToSrt(string text)
    {
        var cues = Sniff(text) switch
        {
            "srt" => Timed(text, vtt: false),
            "vtt" => Timed(text, vtt: true),
            "ass" or "ssa" => Ass(text),
            _ => null,
        };
        return cues is { Count: > 0 } ? Write(cues) : null;
    }

    private static string Normalize(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    // SRT and WebVTT: blank-line separated blocks with a "start --> end" line; anything else (headers, NOTE, STYLE) has none.
    private static List<Cue> Timed(string text, bool vtt)
    {
        var cues = new List<Cue>();
        foreach (var block in Normalize(text).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            var timing = Array.FindIndex(lines, l => l.Contains("-->", StringComparison.Ordinal));
            if (timing < 0)
            {
                continue;
            }

            var times = lines[timing].Split("-->", 2);
            var start = Time(times[0].Trim());
            var end = Time(times[1].Trim().Split(' ', '\t')[0]);
            var body = string.Join('\n', lines.Skip(timing + 1)).Trim();
            if (vtt)
            {
                body = VttTags().Replace(body, string.Empty)
                    .Replace("&lt;", "<", StringComparison.Ordinal)
                    .Replace("&gt;", ">", StringComparison.Ordinal)
                    .Replace("&nbsp;", " ", StringComparison.Ordinal)
                    .Replace("&amp;", "&", StringComparison.Ordinal);
            }

            if (start is not null && end is not null && body.Length > 0)
            {
                cues.Add(new Cue(start.Value, end.Value, body));
            }
        }

        return cues;
    }

    // ASS/SSA: "Dialogue:" lines in [Events], columns named by its "Format:" line; Text is last and may contain commas.
    private static List<Cue> Ass(string text)
    {
        var cues = new List<Cue>();
        string[]? format = null;
        var events = false;
        foreach (var raw in Normalize(text).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                events = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!events)
            {
                continue;
            }

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                format = line["Format:".Length..].Split(',').Select(f => f.Trim()).ToArray();
                continue;
            }

            if (format is null || !line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = line["Dialogue:".Length..].Split(',', format.Length);
            var (startAt, endAt, textAt) = (Column(format, "Start"), Column(format, "End"), Column(format, "Text"));
            if (values.Length != format.Length || startAt < 0 || endAt < 0 || textAt < 0)
            {
                continue;
            }

            var body = AssOverrides().Replace(values[textAt], string.Empty)
                .Replace("\\N", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\h", " ", StringComparison.Ordinal)
                .Trim();
            if (Time(values[startAt].Trim()) is { } start && Time(values[endAt].Trim()) is { } end && body.Length > 0)
            {
                cues.Add(new Cue(start, end, body));
            }
        }

        return cues.OrderBy(c => c.Start).ToList();
    }

    private static int Column(string[] format, string name) => Array.FindIndex(format, f => f.Equals(name, StringComparison.OrdinalIgnoreCase));

    // "01:02:03,456", "01:02:03.456", "02:03.456" (WebVTT) and "1:02:03.45" (ASS centiseconds).
    private static TimeSpan? Time(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var seconds = parts[^1].Split('.');
        var hours = 0;
        if ((parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours))
            || !int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(seconds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            return null;
        }

        if (hours > 99999 || minutes > 59 || whole > 59)
        {
            return null;
        }

        var fraction = seconds.Length > 1 ? seconds[1].PadRight(3, '0')[..3] : "000";
        return int.TryParse(fraction, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? new TimeSpan(0, hours, minutes, whole, milliseconds)
            : null;
    }

    private static string Write(List<Cue> cues)
    {
        var srt = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            srt.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(Format(cues[i].Start)).Append(" --> ").Append(Format(cues[i].End)).Append('\n')
                .Append(cues[i].Text).Append("\n\n");
        }

        return srt.ToString();
    }

    private static string Format(TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");

    // Sniff runs before newlines are normalized, so CRLF files must match too.
    [GeneratedRegex(@"^(\d+[ \t]*\r?\n)?[ \t]*\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}[ \t]*-->", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SrtCue();

    // Everything but <i>, <b>, <u> (voice spans, classes, karaoke timestamps).
    [GeneratedRegex(@"<(?!/?[ibu]>)[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex VttTags();

    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex AssOverrides();

    private sealed record Cue(TimeSpan Start, TimeSpan End, string Text);
}
