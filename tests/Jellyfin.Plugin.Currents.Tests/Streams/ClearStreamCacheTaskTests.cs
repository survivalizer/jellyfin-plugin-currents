using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Users;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class ClearStreamCacheTaskTests
{
    [Fact]
    public async Task Clears_the_stream_cache_and_is_manual()
    {
        var streams = new CountingStreams();
        var task = new ClearStreamCacheTask(streams);

        await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, streams.Clears);
        Assert.Equal(TaskKeys.ClearStreamCache, task.Key);
        Assert.Equal("Currents", task.Category);
        Assert.Empty(task.GetDefaultTriggers());
    }

    private sealed class CountingStreams : IStreamService
    {
        public int Clears { get; private set; }

        public Task<StreamLookup> GetAsync(StreamProfile profile, string type, string stremioId, TimeSpan wait, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public StreamLookup? Peek(StreamProfile profile, string type, string stremioId) => null;

        public void Clear() => Clears++;

        public StreamCacheStats Stats() => new(0, 0, 0);
    }
}
