using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public class CompatWarningTests
{
    private readonly FakeSettings _settings = new();
    private readonly (IActivityManager Instance, InterfaceFake Fake) _activity = InterfaceFake.Create<IActivityManager>();
    private readonly ListLogger<CompatWarning> _logger = new();

    [Fact]
    public async Task An_untested_server_gets_one_activity_warning()
    {
        await new CompatWarning(new CompatState(new Version(13, 0, 0), _settings), _activity.Instance, _logger).StartAsync(CancellationToken.None);

        var entry = (ActivityLog)Assert.Single(_activity.Fake.Calls(nameof(IActivityManager.CreateAsync)))[0]!;
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
        Assert.Contains("13.0.0", entry.Overview, StringComparison.Ordinal);
        Assert.Contains("Run on this untested Jellyfin version", entry.Overview, StringComparison.Ordinal);
        Assert.Contains("per-user versions and track mapping are off, and skip markers are hidden unless \"Also offer markers when a version's length is unknown\" is ticked", entry.Overview, StringComparison.Ordinal);
        Assert.DoesNotContain("skip-marker gate", entry.Overview, StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_tested_server_gets_no_warning()
    {
        await new CompatWarning(new CompatState(new Version(12, 1, 0), _settings), _activity.Instance, _logger).StartAsync(CancellationToken.None);

        Assert.Empty(_activity.Fake.Calls(nameof(IActivityManager.CreateAsync)));
    }

    [Fact]
    public async Task A_failing_activity_log_does_not_throw()
    {
        _activity.Fake.On(nameof(IActivityManager.CreateAsync), _ => Task.FromException(new InvalidOperationException("db locked")));

        await new CompatWarning(new CompatState(new Version(13, 0, 0), _settings), _activity.Instance, _logger).StartAsync(CancellationToken.None);
    }
}
