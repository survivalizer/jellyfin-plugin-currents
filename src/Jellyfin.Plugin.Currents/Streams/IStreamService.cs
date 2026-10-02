using Jellyfin.Plugin.Currents.Users;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Finds and ranks a title's streams for a user profile.</summary>
public interface IStreamService
{
    Task<StreamLookup> GetAsync(StreamProfile profile, string type, string stremioId, TimeSpan wait, CancellationToken cancellationToken);

    StreamLookup? Peek(StreamProfile profile, string type, string stremioId);
}
