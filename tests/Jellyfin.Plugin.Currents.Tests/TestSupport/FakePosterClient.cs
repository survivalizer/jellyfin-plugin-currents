using Jellyfin.Plugin.Currents.Clients.Posters;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakePosterClient : IPosterClient
{
    private readonly List<Uri> _requests = [];

    public PosterImage? Image { get; set; } = new([1, 2, 3], "image/jpeg");

    /// <summary>Gets or sets a per-URL answer; when null, <see cref="Image"/> is returned.</summary>
    public Func<Uri, PosterImage?>? Respond { get; set; }

    /// <summary>Gets or sets a gate every request waits for after it is recorded.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public IReadOnlyList<Uri> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public async Task<PosterImage?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(uri);
        }

        if (Gate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        return Respond is { } respond ? respond(uri) : Image;
    }
}
