using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Checks an AIOStreams manifest URL before it is saved: it must parse and its password must work.</summary>
public static class ManifestValidator
{
    private const string ProbeTitle = "tt0111161";

    /// <summary>Validates a manifest URL.</summary>
    /// <param name="client">The AIOStreams client.</param>
    /// <param name="manifestUrl">The URL to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Null when the config works; otherwise the problem (masked).</returns>
    public static async Task<ManifestProblem?> ValidateAsync(IAioStreamsClient client, string manifestUrl, CancellationToken cancellationToken)
    {
        if (!AioStreamsCredentials.TryParse(manifestUrl, out var credentials, out var error))
        {
            return new ManifestProblem(error ?? "The manifest URL is not valid.", Remote: false);
        }

        try
        {
            await client.SearchAsync(credentials, "movie", ProbeTitle, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is AioStreamsException or HttpRequestException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new ManifestProblem($"AIOStreams did not accept this config: {SecretMasker.Mask(ex.Message)}", Remote: true);
        }
    }

    public static string? HostOf(string? manifestUrl) =>
        AioStreamsCredentials.TryParse(manifestUrl, out var credentials, out _) ? credentials.BaseUri.Host : null;
}
