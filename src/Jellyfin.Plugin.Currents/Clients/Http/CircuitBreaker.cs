using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Pauses calls to an upstream after consecutive failures.</summary>
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Name is fixed by the design; Polly.CircuitBreaker is only a transitive namespace.")]
public sealed class CircuitBreaker
{
    private readonly int _threshold;
    private readonly TimeSpan _openFor;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private int _failures;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;

    public CircuitBreaker(int threshold, TimeSpan openFor, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);
        _threshold = threshold;
        _openFor = openFor;
        _time = time;
    }

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _time.GetUtcNow() < _openUntil;
            }
        }
    }

    public void ThrowIfOpen()
    {
        if (IsOpen)
        {
            throw new CircuitOpenException();
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _failures++;
            if (_failures >= _threshold)
            {
                _openUntil = _time.GetUtcNow() + _openFor;
                _failures = 0;
            }
        }
    }
}
