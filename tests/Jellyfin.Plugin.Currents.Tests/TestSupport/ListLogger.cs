using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

/// <summary>Logger that records every formatted message (and exception text) for assertions.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (exception is not null)
        {
            message += Environment.NewLine + exception;
        }

        lock (_entries)
        {
            _entries.Add((logLevel, message));
        }
    }
}
