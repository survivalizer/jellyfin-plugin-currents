namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Runs one library job at a time (catalog sync, Verify library, Purge Currents content), so a purge is never half-undone by a sync.</summary>
public sealed class LibraryJobGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for the gate.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A handle; dispose it to let the next job run.</returns>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
