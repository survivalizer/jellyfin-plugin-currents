namespace Jellyfin.Plugin.Currents.Common;

/// <summary>The last problems Currents ran into (sync, stream search, skip markers, collections) for the admin diagnostics panel. In memory only; every message is masked.</summary>
public sealed class DiagnosticsLog
{
    internal const int Capacity = 50;
    private readonly TimeProvider _time;
    private readonly Queue<DiagnosticEvent> _events = new();

    public DiagnosticsLog(TimeProvider time) => _time = time;

    public void Record(string area, string message)
    {
        var entry = new DiagnosticEvent(_time.GetUtcNow(), area, SecretMasker.Mask(message));
        lock (_events)
        {
            _events.Enqueue(entry);
            while (_events.Count > Capacity)
            {
                _events.Dequeue();
            }
        }
    }

    /// <summary>Gets the recorded problems, newest first.</summary>
    /// <returns>At most 50 events.</returns>
    public IReadOnlyList<DiagnosticEvent> Recent()
    {
        lock (_events)
        {
            return _events.Reverse().ToList();
        }
    }
}
