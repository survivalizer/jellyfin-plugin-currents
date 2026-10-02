using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Recognises Currents items: a video whose .strm lies under the plugin root and holds a validly signed Currents URL.</summary>
public sealed class CurrentsItemLocator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    private readonly ICurrentsSettings _settings;
    private readonly TtlCache<string, Entry> _cache;

    public CurrentsItemLocator(ICurrentsSettings settings, TimeProvider time)
    {
        _settings = settings;
        _cache = new TtlCache<string, Entry>(time);
    }

    public bool TryGetTitle(BaseItem? item, [NotNullWhen(true)] out CurrentsTitle? title)
    {
        title = null;
        if (item is not Video || item.Path is not { } path || !path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var root = LibraryPaths.FromSettings(_settings).Root + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(root, PathComparison))
        {
            return false;
        }

        if (!_cache.TryGet(path, out var entry))
        {
            entry = new Entry(Read(path));
            _cache.Set(path, entry, CacheTtl);
        }

        title = entry.Title;
        return title is not null;
    }

    private CurrentsTitle? Read(string path)
    {
        try
        {
            var line = File.ReadLines(path).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
            return StrmUrl.TryParse(line, out var type, out var id, out var signature)
                && new StrmSigner(_settings.Current.SigningSecret).Verify(type, id, signature)
                    ? new CurrentsTitle(type, id)
                    : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Entry(CurrentsTitle? Title);
}
