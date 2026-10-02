namespace Jellyfin.Plugin.Currents.Clients.AioStreams;

/// <summary>AIOStreams rejected a request or returned an unreadable response.</summary>
public sealed class AioStreamsException : Exception
{
    public AioStreamsException()
    {
    }

    public AioStreamsException(string message)
        : base(message)
    {
    }

    public AioStreamsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
