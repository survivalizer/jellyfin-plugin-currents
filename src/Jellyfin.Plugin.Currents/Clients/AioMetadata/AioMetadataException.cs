namespace Jellyfin.Plugin.Currents.Clients.AioMetadata;

/// <summary>AIOMetadata returned an error or an unreadable response.</summary>
public sealed class AioMetadataException : Exception
{
    public AioMetadataException()
    {
    }

    public AioMetadataException(string message)
        : base(message)
    {
    }

    public AioMetadataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
