using System.Text;

using Microsoft.Extensions.Logging;

namespace Stateye;

public sealed class VolatileFileLoggerProvider : ILoggerProvider
{
    private readonly Lock _sync = new();
    private readonly StreamWriter _writer;
    private readonly ILogger _logger;
    private bool _disposed;

    public VolatileFileLoggerProvider(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directoryPath))
            Directory.CreateDirectory(directoryPath);

        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        _logger = new VolatileFileLogger(this);
    }

    public ILogger CreateLogger(string categoryName) => _logger;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer.Dispose();
        }
    }

    private void WriteLine(string message)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _writer.Write('[');
            _writer.Write(DateTimeOffset.Now.ToString("O"));
            _writer.Write("] ");
            _writer.WriteLine(message);
        }
    }

    private sealed class VolatileFileLogger(VolatileFileLoggerProvider provider) : ILogger
    {
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

            provider.WriteLine(message);
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
