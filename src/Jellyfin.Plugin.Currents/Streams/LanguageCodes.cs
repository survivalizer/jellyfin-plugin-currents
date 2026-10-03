namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Turns the language names and codes AIOStreams, RemuxDB and subtitle addons use into ISO 639-2/B codes, as ffprobe reports them.</summary>
public static class LanguageCodes
{
    // English name, ISO 639-1, ISO 639-2/T (only where it differs), ISO 639-2/B.
    private static readonly (string Name, string Two, string? Terminology, string Code)[] Known =
    [
        ("English", "en", null, "eng"), ("Japanese", "ja", null, "jpn"), ("French", "fr", "fra", "fre"), ("German", "de", "deu", "ger"),
        ("Spanish", "es", null, "spa"), ("Italian", "it", null, "ita"), ("Portuguese", "pt", null, "por"), ("Russian", "ru", null, "rus"),
        ("Korean", "ko", null, "kor"), ("Chinese", "zh", "zho", "chi"), ("Hindi", "hi", null, "hin"), ("Arabic", "ar", null, "ara"),
        ("Dutch", "nl", "nld", "dut"), ("Polish", "pl", null, "pol"), ("Turkish", "tr", null, "tur"), ("Swedish", "sv", null, "swe"),
        ("Danish", "da", null, "dan"), ("Norwegian", "no", null, "nor"), ("Finnish", "fi", null, "fin"), ("Czech", "cs", "ces", "cze"),
        ("Hungarian", "hu", null, "hun"), ("Greek", "el", "ell", "gre"), ("Hebrew", "he", null, "heb"), ("Thai", "th", null, "tha"),
        ("Vietnamese", "vi", null, "vie"), ("Indonesian", "id", null, "ind"), ("Ukrainian", "uk", null, "ukr"), ("Tamil", "ta", null, "tam"),
        ("Telugu", "te", null, "tel"), ("Malayalam", "ml", null, "mal"), ("Romanian", "ro", "ron", "rum"), ("Bulgarian", "bg", null, "bul"),
        ("Croatian", "hr", null, "hrv"), ("Serbian", "sr", null, "srp"), ("Slovak", "sk", "slk", "slo"), ("Slovenian", "sl", null, "slv"),
        ("Persian", "fa", "fas", "per"), ("Malay", "ms", "msa", "may"), ("Bengali", "bn", null, "ben"), ("Lithuanian", "lt", null, "lit"),
        ("Latvian", "lv", null, "lav"), ("Estonian", "et", null, "est"), ("Icelandic", "is", "isl", "ice"), ("Catalan", "ca", null, "cat"),
    ];

    // OpenSubtitles' own codes.
    private static readonly (string Alias, string Code)[] Aliases = [("pob", "por"), ("pb", "por"), ("ze", "chi")];

    private static readonly Dictionary<string, string> Lookup = BuildLookup();

    public static string? ToIso6392(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var paren = text.IndexOf('(', StringComparison.Ordinal);
        if (paren > 0)
        {
            text = text[..paren].Trim();
        }

        if (Lookup.TryGetValue(text, out var code))
        {
            return code;
        }

        var dash = text.IndexOfAny(['-', '_']);
        if (dash > 0 && Lookup.TryGetValue(text[..dash], out code))
        {
            return code;
        }

        // Any other ISO 639-2 code passes through, as ffprobe would report it.
        return text.Length == 3 && text.All(char.IsAsciiLetterLower) ? text : null;
    }

    private static Dictionary<string, string> BuildLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, two, terminology, code) in Known)
        {
            lookup[name] = code;
            lookup[two] = code;
            lookup[code] = code;
            if (terminology is not null)
            {
                lookup[terminology] = code;
            }
        }

        foreach (var (alias, code) in Aliases)
        {
            lookup[alias] = code;
        }

        return lookup;
    }
}
