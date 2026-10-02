using Jellyfin.Plugin.Currents.Library;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeLibraryItems : ILibraryItems
{
    /// <summary>Gets Jellyfin items for Currents titles, by state id.</summary>
    public Dictionary<string, Guid> Titles { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets items a user can see, by (user, state id).</summary>
    public Dictionary<(Guid User, string StateId), Guid> Existing { get; } = [];

    public HashSet<MediaKind> Addable { get; } = [MediaKind.Movie, MediaKind.Series];

    public List<(MediaKind Kind, string Folder)> Added { get; } = [];

    /// <summary>Gets or sets what AddAsync returns; null means "no library holds the folder".</summary>
    public Func<MediaKind, string, Guid?> AddResult { get; set; } = (_, _) => Guid.NewGuid();

    /// <summary>Gets the ordered pause / add / resume events.</summary>
    public List<string> Events { get; } = [];

    /// <summary>Gets or sets a hook run when monitoring is paused.</summary>
    public Action<MediaKind>? OnPause { get; set; }

    public TaskCompletionSource? AddGate { get; set; }

    public Guid? FindTitle(TitleState title) => Titles.TryGetValue(title.StateId, out var id) ? id : null;

    public IReadOnlyDictionary<string, Guid> FindExisting(Guid userId, IReadOnlyCollection<TitleKey> keys) =>
        keys.Where(k => Existing.ContainsKey((userId, k.StateId))).ToDictionary(k => k.StateId, k => Existing[(userId, k.StateId)], StringComparer.Ordinal);

    public bool CanAdd(Guid userId, MediaKind kind) => userId != Guid.Empty && Addable.Contains(kind);

    /// <summary>Gets the kinds each library id holds.</summary>
    public Dictionary<Guid, MediaKind[]> Libraries { get; } = [];

    public IReadOnlyCollection<MediaKind> KindsIn(Guid libraryId) => Libraries.GetValueOrDefault(libraryId) ?? [];

    public IDisposable PauseMonitoring(MediaKind kind)
    {
        lock (Added)
        {
            Events.Add($"pause:{kind}");
        }

        OnPause?.Invoke(kind);
        return new Resume(this, kind);
    }

    public async Task<Guid?> AddAsync(MediaKind kind, string relativeFolder, CancellationToken cancellationToken)
    {
        lock (Added)
        {
            Added.Add((kind, relativeFolder));
            Events.Add("add");
        }

        if (AddGate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        return AddResult(kind, relativeFolder);
    }

    private sealed class Resume(FakeLibraryItems owner, MediaKind kind) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.Added)
            {
                owner.Events.Add($"resume:{kind}");
            }
        }
    }
}
