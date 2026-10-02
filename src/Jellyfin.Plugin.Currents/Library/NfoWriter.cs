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
            new XElement("title", meta.Name ?? string.Empty),
            Optional("plot", meta.Description),
            Optional("year", MetaMapper.ParseYear(meta)?.ToString(CultureInfo.InvariantCulture)),
            Optional("premiered", MetaMapper.ParseDate(meta.Released)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        foreach (var genre in meta.Genres ?? [])
        {
            root.Add(new XElement("genre", genre));
        }

        foreach (var (type, value) in UniqueIds(key, meta))
        {
            root.Add(new XElement("uniqueid", new XAttribute("type", type), value));
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
        string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value);

    private sealed class StringWriterUtf8 : StringWriter
    {
        public StringWriterUtf8(StringBuilder builder)
            : base(builder, CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
