using System.Diagnostics.CodeAnalysis;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>At startup on an untested Jellyfin version: logs a warning and adds one to Jellyfin's activity log (Dashboard).</summary>
public sealed class CompatWarning : IHostedService
{
    private readonly CompatState _compat;
    private readonly IActivityManager _activity;
    private readonly ILogger<CompatWarning> _logger;

    public CompatWarning(CompatState compat, IActivityManager activity, ILogger<CompatWarning> logger)
    {
        _compat = compat;
        _activity = activity;
        _logger = logger;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A warning must never stop Jellyfin from starting.")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_compat.InTestedRange)
        {
            return;
        }

        try
        {
            var forced = _compat.ForceEnabled;
            var message = forced
                ? $"Currents was tested with Jellyfin {CompatState.TestedFrom} up to (not including) {CompatState.TestedBefore}. This server runs {_compat.Server}; Currents runs anyway because \"Run on this untested Jellyfin version\" is on."
                : $"Currents was tested with Jellyfin {CompatState.TestedFrom} up to (not including) {CompatState.TestedBefore}. This server runs {_compat.Server}, so per-user versions and track mapping are off, and skip markers are hidden unless \"Also offer markers when a version's length is unknown\" is ticked. Titles play their default stream. An admin can turn on \"Run on this untested Jellyfin version\" on the Currents page.";
            _logger.LogWarning("{Message}", message);
            await _activity.CreateAsync(new ActivityLog("Currents: untested Jellyfin version", "CurrentsCompat", Guid.Empty)
            {
                ShortOverview = forced ? "Running anyway (forced on)" : "Per-user versions are off",
                Overview = message,
                LogSeverity = LogLevel.Warning,
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("Could not record the Jellyfin version warning: {Reason}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
