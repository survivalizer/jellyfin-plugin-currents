namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Reads a response body up to a size limit, whether or not the server sent Content-Length.</summary>
public static class BoundedContent
{
    /// <summary>Reads the whole body.</summary>
    /// <param name="content">The response content.</param>
    /// <param name="maxBytes">The largest body accepted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The body, or null when it is larger than <paramref name="maxBytes"/>.</returns>
    public static async Task<byte[]?> ReadAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    return null;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return buffer.ToArray();
        }
    }
}
