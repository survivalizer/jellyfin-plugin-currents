using System.Net;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Decides whether a request comes from the Jellyfin server itself (its ffmpeg / ffprobe).</summary>
public sealed class LocalCallerPolicy
{
    private readonly Func<IEnumerable<IPAddress>> _serverAddresses;

    /// <summary>Initializes a new instance of the <see cref="LocalCallerPolicy"/> class.</summary>
    /// <param name="serverAddresses">Supplies the server's own interface addresses.</param>
    public LocalCallerPolicy(Func<IEnumerable<IPAddress>> serverAddresses) => _serverAddresses = serverAddresses;

    /// <summary>Whether the remote address is loopback or one of the server's own addresses.</summary>
    /// <param name="remote">The remote address, if known.</param>
    /// <returns>True when the caller is the server itself.</returns>
    public bool IsLocal(IPAddress? remote)
    {
        if (remote is null)
        {
            return false;
        }

        remote = Normalize(remote);
        return IPAddress.IsLoopback(remote) || _serverAddresses().Any(a => Normalize(a).Equals(remote));
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
