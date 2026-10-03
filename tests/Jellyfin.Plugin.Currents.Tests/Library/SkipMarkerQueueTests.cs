using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class SkipMarkerQueueTests
{
    private readonly FakeSettings _settings = new();
    private readonly (ITaskManager Instance, InterfaceFake Fake) _tasks = InterfaceFake.Create<ITaskManager>();
    private readonly IScheduledTask _skipMarkers;

    public SkipMarkerQueueTests()
    {
        var (task, taskFake) = InterfaceFake.Create<IScheduledTask>();
        taskFake.On("get_Key", _ => TaskKeys.SkipMarkers);
        _skipMarkers = task;
        var (worker, workerFake) = InterfaceFake.Create<IScheduledTaskWorker>();
        workerFake.On("get_ScheduledTask", _ => task);
        _tasks.Fake.On("get_ScheduledTasks", _ => (IReadOnlyList<IScheduledTaskWorker>)[worker]);
    }

    [Fact]
    public void Queues_skip_markers_after_a_sync_that_wrote_titles()
    {
        Assert.True(SkipMarkerQueue.AfterSync(new SyncReport(2, 0, 0, []), _tasks.Instance, _settings));

        var call = Assert.Single(_tasks.Fake.Calls(nameof(ITaskManager.QueueScheduledTask)));
        Assert.Same(_skipMarkers, call[0]);
    }

    [Fact]
    public void Skips_when_nothing_was_written_or_markers_are_off()
    {
        Assert.False(SkipMarkerQueue.AfterSync(new SyncReport(0, 5, 1, []), _tasks.Instance, _settings));

        _settings.Current.EnableSegments = false;
        Assert.False(SkipMarkerQueue.AfterSync(new SyncReport(2, 0, 0, []), _tasks.Instance, _settings));

        Assert.Empty(_tasks.Fake.Calls(nameof(ITaskManager.QueueScheduledTask)));
    }
}
