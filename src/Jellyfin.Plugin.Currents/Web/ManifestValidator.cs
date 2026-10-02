using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Checks an AIOStreams manifest URL before it is saved: it must parse and its password must work.</summary>
public static class ManifestValidator
{
    private const string ProbeTitle = "tt0111161";

    public static async Task<string?> ValidateAsync(IAioStreamsClient client, string manifestUrl, CancellationToken cancellationToken)
    {
        if (!AioStreamsCredentials.TryParse(manifestUrl, out var credentials, out var error))
        {
            return error;
        }

        try
        {
            await client.SearchAsync(credentials, "movie", ProbeTitle, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is AioStreamsException or HttpRequestException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return $"AIOStreams did not accept this config: {SecretMasker.Mask(ex.Message)}";
        }
    }

    public static string? HostOf(string? manifestUrl) =>
        AioStreamsCredentials.TryParse(manifestUrl, out var credentials, out _) ? credentials.BaseUri.Host : null;
}
