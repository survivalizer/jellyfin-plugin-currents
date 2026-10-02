namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Thrown when calls to a failing upstream are paused by the circuit breaker.</summary>
public sealed class CircuitOpenException : HttpRequestException
{
    public CircuitOpenException()
        : this("The upstream service is failing; calls are paused briefly.")
    {
    }

    public CircuitOpenException(string message)
        : base(message)
    {
    }

    public CircuitOpenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
