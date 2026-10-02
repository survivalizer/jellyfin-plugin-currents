using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeAioStreamsClient : IAioStreamsClient
{
    public SearchOutcome Outcome { get; set; } = new([], []);

    public Exception? Exception { get; set; }

    public int Calls { get; private set; }

    public static StreamResult Stream(string url, Dictionary<string, string>? headers = null) =>
        new() { Url = url, Filename = url, RequestHeaders = headers };

    public Task<SearchOutcome> SearchAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken)
    {
        Calls++;
        return Exception is null ? Task.FromResult(Outcome) : Task.FromException<SearchOutcome>(Exception);
    }
}
