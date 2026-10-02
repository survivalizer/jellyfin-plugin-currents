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
    public void Masks_the_key_in_wrapped_addon_resolve_urls()
    {
        Assert.Equal(
            "https://torrentio.example.com/resolve/realdebrid/***/abcdef0123456789/null/0/Movie.mkv",
            SecretMasker.Mask("https://torrentio.example.com/resolve/realdebrid/SECRETKEY123/abcdef0123456789/null/0/Movie.mkv"));
        Assert.Equal(
            "Torrentio: failed https://t.example.com/resolve/premiumize/***/x",
            SecretMasker.Mask("Torrentio: failed https://t.example.com/resolve/premiumize/abc/x"));
    }

    [Fact]
    public void Masks_long_base64_segments_after_playback()
    {
        Assert.Equal(
            "https://addon.example.com/playback/***/file.mkv",
            SecretMasker.Mask("https://addon.example.com/playback/eyJ0b2tlbiI6IlNFQ1JFVCJ9_-abc=/file.mkv"));
        Assert.Equal(
            "https://addon.example.com/playback/***",
            SecretMasker.Mask("https://addon.example.com/playback/QUJDREVGR0hJSktMTU5PUFFSU1RVVg"));
    }

    [Fact]
    public void Leaves_ordinary_urls_alone()
    {
        Assert.Equal("https://example.com/a/b.json", SecretMasker.Mask("https://example.com/a/b.json"));
        Assert.Equal("https://example.com/playback/short/file.mkv", SecretMasker.Mask("https://example.com/playback/short/file.mkv"));
        Assert.Equal("https://example.com/playback/video.mp4", SecretMasker.Mask("https://example.com/playback/video.mp4"));
        Assert.Equal("https://example.com/docs/resolve", SecretMasker.Mask("https://example.com/docs/resolve"));
        Assert.Equal("https://example.com/resolve/realdebrid", SecretMasker.Mask("https://example.com/resolve/realdebrid"));
        Assert.Equal(string.Empty, SecretMasker.Mask((string?)null));
    }
}
