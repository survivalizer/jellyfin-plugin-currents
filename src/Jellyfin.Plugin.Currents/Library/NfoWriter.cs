using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Metadata;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Writes Kodi-style NFO files that Jellyfin's NFO reader understands.</summary>
public static class NfoWriter
{
    public static string Movie(TitleKey key, StremioMeta meta) => Build("movie", key, meta);

    public static string TvShow(TitleKey key, StremioMeta meta) => Build("tvshow", key, meta);

    private static string Build(string rootName, TitleKey key, StremioMeta meta)
    {
        var root = new XElement(
            rootName,
            new XElement("title", Clean(meta.Name)),
            Optional("plot", meta.Description),
            Optional("year", MetaMapper.ParseYear(meta)?.ToString(CultureInfo.InvariantCulture)),
            Optional("premiered", MetaMapper.ParseDate(meta.Released)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        foreach (var genre in meta.Genres ?? [])
        {
            var clean = Clean(genre);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                root.Add(new XElement("genre", clean));
            }
        }

        foreach (var (type, value) in UniqueIds(key, meta))
        {
            root.Add(new XElement("uniqueid", new XAttribute("type", type), Clean(value)));
        }

        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
        var builder = new StringBuilder();
        using (var stringWriter = new StringWriterUtf8(builder))
        {
            using (var writer = XmlWriter.Create(stringWriter, settings))
            {
                new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(writer);
            }
        }

        return builder.Append('\n').ToString();
    }

    private static IEnumerable<(string Type, string Value)> UniqueIds(TitleKey key, StremioMeta meta)
    {
        yield return (CurrentsProviderIds.Currents, key.StremioId);
        if (key.Provider == "imdb")
        {
            yield return ("imdb", key.Value);
        }
        else if (meta.ImdbId is { Length: > 2 } imdb && imdb.StartsWith("tt", StringComparison.Ordinal))
        {
            yield return ("imdb", imdb);
        }

        if (key.Provider is "tmdb" or "tvdb")
        {
            yield return (key.Provider, key.Value);
        }
    }

    private static XElement? Optional(string name, string? value) =>
        Clean(value) is { } clean && !string.IsNullOrWhiteSpace(clean) ? new XElement(name, clean) : null;

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private sealed class StringWriterUtf8 : StringWriter
    {
        public StringWriterUtf8(StringBuilder builder)
            : base(builder, CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
