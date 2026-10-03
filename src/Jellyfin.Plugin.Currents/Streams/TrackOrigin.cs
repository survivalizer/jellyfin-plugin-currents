namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Where a version's track list came from, best last.</summary>
public enum TrackOrigin
{
    ReleaseName,
    AioStreams,
    RemuxDb,
    Probe,
}
