using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.Diagnostics;

internal sealed class SessionFileLoggerProvider : ILoggerProvider
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly Task _writerTask;

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

        _writerTask = Task.Run(WriteLoopAsync);
    }

    public static string CurrentLogPath { get; private set; } = string.Empty;

    public ILogger CreateLogger(string categoryName) => new SessionFileLogger(this, categoryName);

    public void Dispose()
    {
        _lines.Writer.TryComplete();
        try { _writerTask.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
    }

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        if (!category.StartsWith("CSharpGit", StringComparison.Ordinal)) return;

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var eventText = eventId.Id == 0 && string.IsNullOrWhiteSpace(eventId.Name)
            ? string.Empty
            : $" event={eventId.Id}:{eventId.Name}";
        _lines.Writer.TryWrite(
            $"{timestamp} [{level}] [tid={Environment.CurrentManagedThreadId}] [{category}]{eventText} {message}");

        if (exception is not null)
        {
            _lines.Writer.TryWrite(exception.ToString());
        }
    }

    private async Task WriteLoopAsync()
    {
        await using var stream = new FileStream(
            CurrentLogPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 16 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(stream);
        var pending = 0;

        await foreach (var line in _lines.Reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(line);
            pending++;
            if (pending >= 32)
            {
                await writer.FlushAsync();
                pending = 0;
            }
        }

        await writer.FlushAsync();
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
