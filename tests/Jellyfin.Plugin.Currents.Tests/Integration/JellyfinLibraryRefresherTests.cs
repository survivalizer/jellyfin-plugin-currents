using System.Reflection;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class JellyfinLibraryRefresherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "currents-refresher-" + Guid.NewGuid().ToString("N"));

    public JellyfinLibraryRefresherTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Queues_one_library_scan_when_existing_folders_have_not_been_scanned_yet()
    {
        var movies = Directory.CreateDirectory(Path.Combine(_root, "Movies")).FullName;
        var shows = Directory.CreateDirectory(Path.Combine(_root, "Shows")).FullName;
        var library = RecordingProxy.Create<ILibraryManager>();
        var logger = new ListLogger<JellyfinLibraryRefresher>();
        var refresher = new JellyfinLibraryRefresher(library.Instance, RecordingProxy.Create<IFileSystem>().Instance, logger);

        await refresher.RefreshAsync([movies, shows], CancellationToken.None);

        Assert.Equal(1, library.Count(nameof(ILibraryManager.QueueLibraryScan)));
        Assert.Equal(2, library.Count(nameof(ILibraryManager.FindByPath)));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains(movies, StringComparison.Ordinal)
            && e.Message.Contains("has not been scanned into a Jellyfin library yet; queued a library scan", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Queues_the_library_scan_at_most_once_per_process()
    {
        var movies = Directory.CreateDirectory(Path.Combine(_root, "Movies")).FullName;
        var library = RecordingProxy.Create<ILibraryManager>();
        var logger = new ListLogger<JellyfinLibraryRefresher>();
        var refresher = new JellyfinLibraryRefresher(library.Instance, RecordingProxy.Create<IFileSystem>().Instance, logger);

        await refresher.RefreshAsync([movies], CancellationToken.None);
        await refresher.RefreshAsync([movies], CancellationToken.None);
        await refresher.RefreshAsync([movies], CancellationToken.None);

        Assert.Equal(1, library.Count(nameof(ILibraryManager.QueueLibraryScan)));
        Assert.Equal(3, library.Count(nameof(ILibraryManager.FindByPath)));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains("is not part of any Jellyfin library yet", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Skips_missing_folders_silently()
    {
        var library = RecordingProxy.Create<ILibraryManager>();
        var logger = new ListLogger<JellyfinLibraryRefresher>();
        var refresher = new JellyfinLibraryRefresher(library.Instance, RecordingProxy.Create<IFileSystem>().Instance, logger);

        await refresher.RefreshAsync([Path.Combine(_root, "Missing")], CancellationToken.None);

        Assert.Equal(0, library.Count(nameof(ILibraryManager.QueueLibraryScan)));
        Assert.Equal(0, library.Count(nameof(ILibraryManager.FindByPath)));
        Assert.Empty(logger.Entries);
    }

    /// <summary>Interface fake that records calls and returns default values (FindByPath returns null).</summary>
    public class RecordingProxy : DispatchProxy
    {
        private readonly List<string> _calls = [];

        public static Recorder<T> Create<T>()
            where T : class
        {
            var instance = Create<T, RecordingProxy>();
            return new Recorder<T>(instance, (RecordingProxy)(object)instance);
        }

        public int Count(string method)
        {
            lock (_calls)
            {
                return _calls.Count(c => c == method);
            }
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            lock (_calls)
            {
                _calls.Add(targetMethod.Name);
            }

            var type = targetMethod.ReturnType;
            return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
        }
    }

    public sealed record Recorder<T>(T Instance, RecordingProxy Proxy)
    {
        public int Count(string method) => Proxy.Count(method);
    }
}
