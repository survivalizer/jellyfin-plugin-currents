using Jellyfin.Plugin.Currents.Common;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Common;

public class SecretMaskerTests
{
    [Fact]
    public void Masks_aiostreams_uuid_and_password()
    {
        var masked = SecretMasker.Mask("https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/eyJpdiI6IjEyMyJ9/manifest.json");

        Assert.Equal("https://aio.example.com/stremio/***/***/manifest.json", masked);
    }

    [Fact]
    public void Masks_aiometadata_uuid()
    {
        var masked = SecretMasker.Mask("https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json");

        Assert.Equal("https://meta.example.com/stremio/***/manifest.json", masked);
    }

    [Fact]
    public void Masks_signatures_and_playback_tokens()
    {
        Assert.Equal(
            "http://127.0.0.1:8096/Currents/play/movie/tt1?sig=***",
            SecretMasker.Mask("http://127.0.0.1:8096/Currents/play/movie/tt1?sig=abcDEF123"));
        Assert.Equal(
            "https://aio.example.com/api/v1/debrid/playback/***/x/file.mkv",
            SecretMasker.Mask("https://aio.example.com/api/v1/debrid/playback/ENCRYPTEDSTUFF/x/file.mkv"));
    }

    [Fact]
    public void Leaves_ordinary_urls_alone()
    {
        Assert.Equal("https://example.com/a/b.json", SecretMasker.Mask("https://example.com/a/b.json"));
        Assert.Equal(string.Empty, SecretMasker.Mask((string?)null));
    }
}
