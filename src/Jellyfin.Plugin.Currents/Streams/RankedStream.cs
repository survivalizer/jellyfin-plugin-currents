using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A stream with its stable key (<see cref="StreamIdentity"/>), in a user's ranked order.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Not a System.IO.Stream; 'stream' is the AIOStreams domain term for a playable source.")]
public sealed record RankedStream(string Key, StreamResult Result);
