using System.Globalization;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.Diagnostics;

internal sealed class SessionFileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public SessionFileLoggerProvider()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CSharpGit",
            "Logs");
        Directory.CreateDirectory(directory);
        DeleteOldLogs(directory, keep: 19);

        CurrentLogPath = Path.Combine(
            directory,
            $"csharpgit-{DateTime.Now:yyyyMMdd-HHmmss}-p{Environment.ProcessId}.log");

        _writer = new StreamWriter(new FileStream(
            CurrentLogPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public static string CurrentLogPath { get; private set; } = string.Empty;

    public ILogger CreateLogger(string categoryName) => new SessionFileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        if (!category.StartsWith("CSharpGit", StringComparison.Ordinal)) return;

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var eventText = eventId.Id == 0 && string.IsNullOrWhiteSpace(eventId.Name)
            ? string.Empty
            : $" event={eventId.Id}:{eventId.Name}";
        var line = $"{timestamp} [{level}] [tid={Environment.CurrentManagedThreadId}] [{category}]{eventText} {message}";

        lock (_gate)
        {
            _writer.WriteLine(line);
            if (exception is not null)
            {
                _writer.WriteLine(exception);
            }
        }
    }

    private static void DeleteOldLogs(string directory, int keep)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "csharpgit-*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(keep))
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class SessionFileLogger(SessionFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
