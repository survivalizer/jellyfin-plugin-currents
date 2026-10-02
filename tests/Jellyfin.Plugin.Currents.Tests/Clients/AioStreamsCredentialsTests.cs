using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class AioStreamsCredentialsTests
{
    private const string Uuid = "0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d";

    [Fact]
    public void Parses_uuid_password_and_base()
    {
        Assert.True(AioStreamsCredentials.TryParse($"https://aio.example.com/stremio/{Uuid}/eyJpdiI6IjEyMyJ9/manifest.json", out var creds, out _));

        Assert.Equal("https://aio.example.com/", creds!.BaseUri.ToString());
        Assert.Equal(Uuid, creds.Uuid);
        Assert.Equal("eyJpdiI6IjEyMyJ9", creds.Password);
    }

    [Fact]
    public void Keeps_a_path_prefix_in_the_base()
    {
        Assert.True(AioStreamsCredentials.TryParse($"https://host.example.com/aio/stremio/{Uuid}/pw/manifest.json", out var creds, out _));

        Assert.Equal("https://host.example.com/aio/", creds!.BaseUri.ToString());
    }

    [Fact]
    public void Builds_search_url_and_basic_auth()
    {
        AioStreamsCredentials.TryParse($"https://aio.example.com/stremio/{Uuid}/pw/manifest.json", out var creds, out _);

        Assert.Equal("https://aio.example.com/api/v1/search?type=series&id=tt0944947%3A1%3A2", creds!.Search("series", "tt0944947:1:2").AbsoluteUri);
        var auth = creds.BasicAuth();
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal($"{Uuid}:pw", Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)));
    }

    [Fact]
    public void ToString_does_not_leak_secrets()
    {
        AioStreamsCredentials.TryParse($"https://aio.example.com/stremio/{Uuid}/secretpw/manifest.json", out var creds, out _);

        Assert.DoesNotContain(Uuid, creds!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secretpw", creds.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://aio.example.com/stremio/u/myalias/manifest.json")]
    [InlineData("https://aio.example.com/stremio/not-a-uuid/pw/manifest.json")]
    [InlineData("https://aio.example.com/manifest.json")]
    [InlineData("garbage")]
    public void Rejects_urls_without_uuid_and_password(string url)
    {
        Assert.False(AioStreamsCredentials.TryParse(url, out var creds, out var error));
        Assert.Null(creds);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Fingerprint_identifies_a_config_without_revealing_it()
    {
        AioStreamsCredentials.TryParse("https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/secretpw/manifest.json", out var a, out _);
        AioStreamsCredentials.TryParse("https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/secretpw/manifest.json", out var same, out _);
        AioStreamsCredentials.TryParse("https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/otherpw/manifest.json", out var other, out _);

        Assert.Matches("^[0-9a-f]{24}$", a!.Fingerprint());
        Assert.Equal(a.Fingerprint(), same!.Fingerprint());
        Assert.NotEqual(a.Fingerprint(), other!.Fingerprint());
        Assert.DoesNotContain("secretpw", a.Fingerprint(), StringComparison.Ordinal);
    }
}
