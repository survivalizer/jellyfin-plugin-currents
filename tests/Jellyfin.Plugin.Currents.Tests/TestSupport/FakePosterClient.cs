using Jellyfin.Plugin.Currents.Clients.Posters;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakePosterClient : IPosterClient
{
    public PosterImage? Image { get; set; } = new([1, 2, 3], "image/jpeg");

    public List<Uri> Requests { get; } = [];

    public Task<PosterImage?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        Requests.Add(uri);
        return Task.FromResult(Image);
    }
}
