using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>ffprobe results per stream key, so each stream is probed once.</summary>
public sealed class ProbeCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private readonly TtlCache<string, ProbedMedia> _cache;

    public ProbeCache(TimeProvider time) => _cache = new TtlCache<string, ProbedMedia>(time);

    public bool TryGet(string streamKey, [NotNullWhen(true)] out ProbedMedia? media) => _cache.TryGet(streamKey, out media);

    public void Set(string streamKey, ProbedMedia media) => _cache.Set(streamKey, media, Ttl);
}
