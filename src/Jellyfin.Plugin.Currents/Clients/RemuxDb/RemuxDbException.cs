namespace Jellyfin.Plugin.Currents.Clients.RemuxDb;

/// <summary>RemuxDB could not be read (error status, invalid or oversized body).</summary>
public sealed class RemuxDbException : Exception
{
    public RemuxDbException()
    {
    }

    public RemuxDbException(string message)
        : base(message)
    {
    }

    public RemuxDbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
