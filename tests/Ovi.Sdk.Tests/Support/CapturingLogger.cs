using Microsoft.Extensions.Logging;

namespace Ovi.Sdk.Tests.Support;

/// <summary>An ILoggerFactory/ILogger test double that records every log entry.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public List<(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(
        string category,
        List<(string, LogLevel, EventId, string, Exception?)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entries)
            {
                entries.Add((category, logLevel, eventId, formatter(state, exception), exception));
            }
        }
    }
}
