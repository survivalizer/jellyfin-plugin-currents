using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>
/// Decides whether a title's stored markers fit one version. Jellyfin stores markers per item, but each version is a
/// different file: its real runtime must be within the admin's tolerance of the runtime the markers belong to.
/// </summary>
public sealed class SegmentGate
{
    private readonly SegmentStore _store;
    private readonly ICurrentsSettings _settings;

    public SegmentGate(SegmentStore store, ICurrentsSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    /// <summary>Gets the tolerance in percent: the setting clamped to 1–10 (2 when it is not a number).</summary>
    public double TolerancePercent =>
        double.IsFinite(_settings.Current.SegmentTolerancePercent) ? Math.Clamp(_settings.Current.SegmentTolerancePercent, 1, 10) : 2;

    /// <summary>Gets whether the title's markers apply to a version.</summary>
    /// <param name="title">The title.</param>
    /// <param name="versionRunTimeTicks">The version's real runtime (probe, RemuxDB or AIOStreams), or null when unknown.</param>
    /// <returns>True when the markers may be offered.</returns>
    public bool Allows(CurrentsTitle title, long? versionRunTimeTicks)
    {
        var config = _settings.Current;
        if (!config.EnableSegments)
        {
            return false;
        }

        var reference = _store.ReferenceTicks(title);
        if (reference is not > 0 || versionRunTimeTicks is not > 0)
        {
            return config.SegmentsWhenRuntimeUnknown;
        }

        return Math.Abs(versionRunTimeTicks.Value - reference.Value) <= reference.Value * TolerancePercent / 100;
    }
}
