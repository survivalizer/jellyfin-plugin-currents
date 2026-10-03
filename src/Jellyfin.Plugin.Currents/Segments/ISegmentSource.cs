namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>One remote skip-marker database.</summary>
public interface ISegmentSource
{
    /// <summary>Gets the source's name for logs and diagnostics.</summary>
    string Name { get; }

    /// <summary>Gets the priority. For each marker kind, the source with the lowest value that has a marker of that kind wins.</summary>
    int Priority { get; }

    /// <summary>Gets whether this source can answer for the request with the current settings.</summary>
    /// <param name="request">The ids.</param>
    /// <returns>True when the source should be asked.</returns>
    bool AppliesTo(SegmentRequest request);

    /// <summary>Looks the title up.</summary>
    /// <param name="request">The ids.</param>
    /// <param name="targetRunTimeTicks">The runtime AIOMetadata gives the title, if any; used to pick a release.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The markers, or null when the source has none for the title.</returns>
    /// <exception cref="SegmentSourceException">The source could not be asked (network, rate limit, unreadable answer).</exception>
    Task<SourceMarkers?> GetAsync(SegmentRequest request, long? targetRunTimeTicks, CancellationToken cancellationToken);
}
