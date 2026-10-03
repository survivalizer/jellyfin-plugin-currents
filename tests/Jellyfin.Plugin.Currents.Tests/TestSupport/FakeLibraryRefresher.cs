using Jellyfin.Plugin.Currents.Library;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeLibraryRefresher : ILibraryRefresher
{
    public List<string[]> Refreshed { get; } = [];

    public Task RefreshAsync(IReadOnlyCollection<string> folders, CancellationToken cancellationToken)
    {
        Refreshed.Add([.. folders]);
        return Task.CompletedTask;
    }
}
