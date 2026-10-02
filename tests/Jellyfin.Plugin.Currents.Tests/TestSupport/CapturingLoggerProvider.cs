using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

/// <summary>Logger provider that records every message and the category that logged it.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(string Category, string Message)> _entries = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_entries)
            {
                return _entries.Select(e => e.Message).ToList();
            }
        }
    }

    public IReadOnlyList<string> Categories
    {
        get
        {
            lock (_entries)
            {
                return _entries.Select(e => e.Category).ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._entries)
            {
                owner._entries.Add((category, formatter(state, exception)));
            }
        }
    }
}
