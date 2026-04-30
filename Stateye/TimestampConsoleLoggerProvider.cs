using Microsoft.Extensions.Logging;

public sealed class TimestampConsoleLoggerProvider : ILoggerProvider
{
    private static readonly ILogger LoggerInstance = new TimestampConsoleLogger();

    public ILogger CreateLogger(string categoryName) => LoggerInstance;

    public void Dispose()
    {
    }

    private sealed class TimestampConsoleLogger : ILogger
    {
        private static readonly Lock Sync = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.None)
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message))
                message = exception?.ToString();

            if (string.IsNullOrEmpty(message))
                return;

            lock (Sync)
            {
                if (logLevel == LogLevel.Critical)
                {
                    var previousColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                    Console.ForegroundColor = previousColor;
                    return;
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
