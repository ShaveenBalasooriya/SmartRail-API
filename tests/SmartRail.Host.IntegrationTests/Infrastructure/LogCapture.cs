using Microsoft.Extensions.Logging;

namespace SmartRail.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Captures every log entry the Host writes, so the diagnostic-logging rules can be asserted
/// against what was really written rather than against what the code appears to do.
/// </summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly List<LogEntry> _entries = [];
    private readonly Lock _gate = new();

    /// <summary>The captured entries, oldest first.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Every captured entry rendered as text, for "must not contain" assertions.</summary>
    public string AllText => string.Join("\n", Entries.Select(entry => entry.ToString()));

    /// <summary>Ingests an entry from the ServiceProvider-held test logger.</summary>
    public void Add(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(LogCapture capture, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => new Scope();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
            {
                foreach (var property in properties)
                {
                    values[property.Key] = property.Value;
                }
            }

            capture.Add(new LogEntry(category, logLevel, formatter(state, exception), exception, values));
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

/// <summary>One captured log entry: its category, level, message, exception and structured values.</summary>
public sealed record LogEntry(
    string Category,
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Values)
{
    /// <summary>The value of a structured property, or null when the entry does not carry it.</summary>
    public object? Value(string key) => Values.TryGetValue(key, out var value) ? value : null;

    public override string ToString() =>
        $"{Level} {Category}: {Message} {Exception} {string.Join(",", Values.Select(pair => $"{pair.Key}={pair.Value}"))}";
}
