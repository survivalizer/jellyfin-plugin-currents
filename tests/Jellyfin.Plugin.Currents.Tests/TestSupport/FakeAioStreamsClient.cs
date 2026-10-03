using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeAioStreamsClient : IAioStreamsClient
{
    private int _calls;
    private int _subtitleCalls;

    public SearchOutcome Outcome { get; set; } = new([], []);

    public Exception? Exception { get; set; }

    /// <summary>Gets or sets a gate: when set, searches wait for it instead of returning <see cref="Outcome"/>.</summary>
    public TaskCompletionSource<SearchOutcome>? Gate { get; set; }

    public List<StremioSubtitle> SubtitleList { get; set; } = [];

    public Exception? SubtitleException { get; set; }

    public int SubtitleCalls => Volatile.Read(ref _subtitleCalls);

    public int Calls => Volatile.Read(ref _calls);

    public AioStreamsCredentials? LastCredentials { get; private set; }

    public static StreamResult Stream(string url, Dictionary<string, string>? headers = null) =>
        new() { Url = url, Filename = url, RequestHeaders = headers };

    public Task<SearchOutcome> SearchAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastCredentials = credentials;
        if (Gate is { } gate)
        {
            return gate.Task;
        }

        return Exception is null ? Task.FromResult(Outcome) : Task.FromException<SearchOutcome>(Exception);
    }

    public Task<IReadOnlyList<StremioSubtitle>> SubtitlesAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _subtitleCalls);
        LastCredentials = credentials;
        return SubtitleException is null
            ? Task.FromResult<IReadOnlyList<StremioSubtitle>>(SubtitleList)
            : Task.FromException<IReadOnlyList<StremioSubtitle>>(SubtitleException);
    }
}
