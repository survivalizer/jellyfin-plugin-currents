using Jellyfin.Plugin.Currents.Clients.RemuxDb;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeRemuxDbClient : IRemuxDbClient
{
    private int _calls;

    public Dictionary<string, IReadOnlyList<RemuxDbVersion>> Versions { get; } = new(StringComparer.Ordinal);

    public Exception? Exception { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public Task<IReadOnlyList<RemuxDbVersion>> VersionsAsync(string externalId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Exception is not null)
        {
            return Task.FromException<IReadOnlyList<RemuxDbVersion>>(Exception);
        }

        return Task.FromResult(Versions.TryGetValue(externalId, out var versions) ? versions : (IReadOnlyList<RemuxDbVersion>)[]);
    }
}
