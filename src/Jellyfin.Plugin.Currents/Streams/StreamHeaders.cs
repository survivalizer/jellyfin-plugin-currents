namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Request headers a stream needs (AIOStreams requestHeaders): cleaned, and stripped of credentials when a request leaves the stream's origin. Values are credentials: never logged.</summary>
public static class StreamHeaders
{
    // Set by the HTTP stack or by the proxy itself, never taken from upstream data.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade", "TE", "Trailer", "Proxy-Connection", "Range", "If-Range", "Expect", "Accept-Encoding",
    };

    private static readonly HashSet<string> Credentials = new(StringComparer.OrdinalIgnoreCase) { "Authorization", "Cookie", "Proxy-Authorization" };

    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    public static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string>? headers)
    {
        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers ?? None)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed) || Reserved.Contains(trimmed) || !IsToken(trimmed) || value is null || value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
            {
                continue;
            }

            clean[trimmed] = value.Trim();
        }

        return clean;
    }

    /// <summary>The headers to send to <paramref name="target"/> for a stream that started at <paramref name="origin"/>: credentials only on the same scheme, host and port.</summary>
    public static IReadOnlyDictionary<string, string> For(IReadOnlyDictionary<string, string> headers, Uri origin, Uri target) =>
        SameOrigin(origin, target)
            ? headers
            : headers.Where(h => !Credentials.Contains(h.Key)).ToDictionary(h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase);

    public static void Apply(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        a.Scheme == b.Scheme && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;

    // RFC 9110 token characters.
    private static bool IsToken(string name) =>
        name.All(c => c is > ' ' and < '\u007f' && !"()<>@,;:\\\"/[]?={}".Contains(c, StringComparison.Ordinal));
}
