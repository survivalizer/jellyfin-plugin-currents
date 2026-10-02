namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Ranked streams for a title, or a user-facing reason there are none.</summary>
public sealed record StreamLookup(IReadOnlyList<RankedStream> Streams, string? Error)
{
    public static StreamLookup Fail(string error) => new([], error);
}
