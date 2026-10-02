using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>HMAC signatures for resolve URLs so only Currents-written .strm files can trigger stream lookups.</summary>
public sealed class StrmSigner
{
    private const int SignatureBytes = 16;
    private readonly byte[] _key;

    public StrmSigner(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("The signing secret is empty.", nameof(secret));
        }

        _key = Convert.FromBase64String(secret);
    }

    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string Sign(string type, string stremioId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{type}/{stremioId}"));
        return Base64Url.EncodeToString(mac.AsSpan(0, SignatureBytes));
    }

    public bool Verify(string type, string stremioId, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(type, stremioId)),
            Encoding.ASCII.GetBytes(signature));
    }

    public string StrmUrl(string baseUrl, string type, string stremioId) =>
        $"{baseUrl.TrimEnd('/')}/Currents/play/{type}/{Uri.EscapeDataString(stremioId)}?sig={Sign(type, stremioId)}";
}
