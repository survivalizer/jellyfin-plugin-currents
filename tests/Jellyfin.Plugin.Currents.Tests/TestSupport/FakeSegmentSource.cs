using Jellyfin.Plugin.Currents.Segments;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeSegmentSource(string name, int priority) : ISegmentSource
{
    public bool Applies { get; init; } = true;

    public SourceMarkers? Answer { get; set; }

    public Exception? Error { get; set; }

    public int Calls { get; private set; }

    public List<long?> Targets { get; } = [];

    public string Name => name;

    public int Priority => priority;

    public bool AppliesTo(SegmentRequest request) => Applies;

    public Task<SourceMarkers?> GetAsync(SegmentRequest request, long? targetRunTimeTicks, CancellationToken cancellationToken)
    {
        Calls++;
        Targets.Add(targetRunTimeTicks);
        return Error is null ? Task.FromResult(Answer) : Task.FromException<SourceMarkers?>(Error);
    }
}
