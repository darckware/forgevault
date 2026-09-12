using Microsoft.Extensions.Logging;

namespace ForgeVault.Infrastructure.Tests.TestSupport;

// Minimal ILogger<T> fake that records every formatted message, so tests can assert on
// what actually got logged — used by the "never logs plaintext or DEK" test cases
// (docs/architecture/IMPLEMENTATION_READINESS.md §5, milestone M2).
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
