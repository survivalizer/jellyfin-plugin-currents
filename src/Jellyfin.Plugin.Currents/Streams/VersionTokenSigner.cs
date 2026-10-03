using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Signs and verifies the expiring tokens in version URLs (/Currents/play/s/{token}).</summary>
public sealed class VersionTokenSigner
{
    private const int SignatureBytes = 16;
    private const int MaxTokenLength = 2048;
    private static readonly byte[] Domain = "currents/version-token/v1|"u8.ToArray();
    private static readonly byte[] SubtitleDomain = "currents/subtitle-token/v1|"u8.ToArray();
    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public VersionTokenSigner(string secret, TimeProvider time)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("The signing secret is empty.", nameof(secret));
        }

        _key = Convert.FromBase64String(secret);
        _time = time;
    }

    public static string PathFor(string token) => $"Currents/play/s/{token}";

    public static string PathForSubtitle(string token) => $"Currents/subtitles/{token}.srt";

    public string Create(VersionTicket ticket, TimeSpan lifetime) => Encode(Domain, ticket, null, lifetime);

    /// <summary>A token naming one subtitle (by key) of one version; signed under its own domain.</summary>
    public string CreateSubtitle(VersionTicket ticket, string subtitleKey, TimeSpan lifetime) => Encode(SubtitleDomain, ticket, subtitleKey, lifetime);

    public bool TryRead(string? token, [NotNullWhen(true)] out VersionTicket? ticket) => TryDecode(Domain, token, out ticket, out _);

    public bool TryReadSubtitle(string? token, [NotNullWhen(true)] out VersionTicket? ticket, [NotNullWhen(true)] out string? subtitleKey)
    {
        if (TryDecode(SubtitleDomain, token, out ticket, out subtitleKey) && !string.IsNullOrEmpty(subtitleKey))
        {
            return true;
        }

        (ticket, subtitleKey) = (null, null);
        return false;
    }

    private string Encode(byte[] domain, VersionTicket ticket, string? subtitleKey, TimeSpan lifetime)
    {
        var payload = new Payload(ticket.UserId.ToString("N"), ticket.Type, ticket.StremioId, ticket.StreamKey, (_time.GetUtcNow() + lifetime).ToUnixTimeSeconds(), subtitleKey);
        var body = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return body + "." + Sign(domain, body);
    }

    private bool TryDecode(byte[] domain, string? token, [NotNullWhen(true)] out VersionTicket? ticket, out string? subtitleKey)
    {
        ticket = null;
        subtitleKey = null;
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var dot = token.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return false;
        }

        var body = token[..dot];
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(domain, body)), Encoding.ASCII.GetBytes(token[(dot + 1)..])))
        {
            return false;
        }

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(Base64Url.DecodeFromChars(body));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return false;
        }

        if (payload is null
            || payload.X <= _time.GetUtcNow().ToUnixTimeSeconds()
            || !Guid.TryParseExact(payload.U, "N", out var userId)
            || string.IsNullOrEmpty(payload.T)
            || string.IsNullOrEmpty(payload.I)
            || string.IsNullOrEmpty(payload.K))
        {
            return false;
        }

        ticket = new VersionTicket(userId, payload.T, payload.I, payload.K);
        subtitleKey = payload.S;
        return true;
    }

    private string Sign(byte[] domain, string body)
    {
        var data = new byte[domain.Length + Encoding.ASCII.GetByteCount(body)];
        domain.CopyTo(data, 0);
        Encoding.ASCII.GetBytes(body, data.AsSpan(domain.Length));
        var mac = HMACSHA256.HashData(_key, data);
        return Base64Url.EncodeToString(mac.AsSpan(0, SignatureBytes));
    }

    private sealed record Payload(string U, string T, string I, string K, long X, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? S = null);
}
