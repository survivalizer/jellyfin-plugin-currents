using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Metadata;
using Jellyfin.Plugin.Currents.Streams;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>
/// Finds a title's skip markers: asks every source that applies (in parallel), keeps for each kind the markers of the
/// highest-priority source that has that kind, sanitises them against the reference runtime and caches the result.
/// A source that cannot be asked fails the whole lookup, so Jellyfin keeps the markers it stored earlier.
/// </summary>
public sealed class SegmentService
{
    internal const long MinLengthMs = 1000;
    private readonly IReadOnlyList<ISegmentSource> _sources;
    private readonly SegmentStore _store;
    private readonly MetaCache _metas;
    private readonly DiagnosticsLog _diagnostics;
    private readonly ILogger<SegmentService> _logger;

    public SegmentService(IEnumerable<ISegmentSource> sources, SegmentStore store, MetaCache metas, DiagnosticsLog diagnostics, ILogger<SegmentService> logger)
    {
        _sources = sources.OrderBy(s => s.Priority).ToList();
        _store = store;
        _metas = metas;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<SegmentLookup> GetAsync(CurrentsTitle title, CancellationToken cancellationToken)
    {
        if (_store.TryGetFresh(title, out var cached))
        {
            return cached;
        }

        if (!SegmentRequest.TryCreate(title, out var request))
        {
            return SegmentLookup.None;
        }

        var applicable = _sources.Where(s => s.AppliesTo(request)).ToList();
        if (applicable.Count == 0)
        {
            return SegmentLookup.None;
        }

        var meta = await _metas.GetAsync(title.Type, title.SeriesId, cancellationToken).ConfigureAwait(false);
        var target = MetaMapper.ParseRuntimeTicks(meta?.Runtime);
        var answers = await Task.WhenAll(applicable.Select(s => AskAsync(s, request, target, cancellationToken))).ConfigureAwait(false);

        var failed = answers.Where(a => a.Error is not null).ToList();
        if (failed.Count > 0)
        {
            foreach (var answer in failed)
            {
                _diagnostics.Record("Skip markers", $"{title.StremioId}: {answer.Error}");
            }

            _logger.LogWarning("Skip markers for {Id} not updated: {Errors}", title.StremioId, string.Join("; ", failed.Select(a => a.Error)));
            throw new SegmentSourceException($"Skip-marker sources unavailable: {string.Join(", ", failed.Select(a => a.Source.Name))}.");
        }

        var lookup = Merge(answers, target);
        _store.Set(title, lookup);
        _logger.LogDebug("Skip markers for {Id}: {Count}", title.StremioId, lookup.Markers.Count);
        return lookup;
    }

    /// <summary>Drops or clamps markers that cannot belong to a file of the reference length; drops open-ended markers when the length is unknown and markers under 1 s.</summary>
    /// <param name="markers">The merged markers.</param>
    /// <param name="referenceTicks">The reference runtime, if known.</param>
    /// <returns>The usable markers, each with an end, sorted by start.</returns>
    internal static IReadOnlyList<SkipMarker> Sanitise(IEnumerable<SkipMarker> markers, long? referenceTicks)
    {
        var referenceMs = referenceTicks / TimeSpan.TicksPerMillisecond;
        var result = new List<SkipMarker>();
        foreach (var marker in markers)
        {
            var start = Math.Max(0, marker.StartMs);
            var end = marker.EndMs ?? referenceMs;
            if (end is null)
            {
                continue;
            }

            if (referenceMs is { } length)
            {
                if (start >= length)
                {
                    continue;
                }

                end = Math.Min(end.Value, length);
            }

            if (end.Value - start >= MinLengthMs)
            {
                result.Add(new SkipMarker(marker.Kind, start, end));
            }
        }

        return result.OrderBy(m => m.StartMs).ThenBy(m => m.Kind).ToList();
    }

    private static SegmentLookup Merge(IReadOnlyList<Answer> answers, long? target)
    {
        var chosen = new List<SkipMarker>();
        long? reference = null;
        foreach (var kind in Enum.GetValues<MarkerKind>())
        {
            var winner = answers
                .Where(a => a.Markers is not null && a.Markers.Markers.Any(m => m.Kind == kind))
                .OrderBy(a => a.Source.Priority)
                .FirstOrDefault();
            if (winner?.Markers is null)
            {
                continue;
            }

            chosen.AddRange(winner.Markers.Markers.Where(m => m.Kind == kind));
            reference ??= winner.Markers.ReferenceTicks;
        }

        reference ??= target is > 0 ? target : null;
        return new SegmentLookup(Sanitise(chosen, reference), reference);
    }

    private static async Task<Answer> AskAsync(ISegmentSource source, SegmentRequest request, long? target, CancellationToken cancellationToken)
    {
        try
        {
            return new Answer(source, await source.GetAsync(request, target, cancellationToken).ConfigureAwait(false), null);
        }
        catch (SegmentSourceException ex)
        {
            return new Answer(source, null, ex.Message);
        }
    }

    private sealed record Answer(ISegmentSource Source, SourceMarkers? Markers, string? Error);
}
