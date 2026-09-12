using Microsoft.Extensions.Logging;

namespace ForgeVault.Security.Tests;

// Same pattern as ForgeVault.Infrastructure.Tests.TestSupport.CapturingLogger (M2), but
// wired as an ILoggerProvider so it can capture everything the real Api host logs during
// an end-to-end HTTP request, not just calls made directly against one service.
internal sealed class CapturingLoggerProvider(List<string> messages) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            lock (messages)
            {
                messages.Add(message);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
