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
    private readonly string _directory;
    private readonly string _filePath;
    private readonly Task _writerTask;
    private int _enabled;
    private int _minimumLevel;

    public SessionFileLoggerProvider(
        bool enabled = false,
        LogLevel minimumLevel = LogLevel.Information)
    {
        ValidateLevel(minimumLevel);

        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CSharpGit",
            "Logs");
        _filePath = Path.Combine(
            _directory,
            $"csharpgit-{DateTime.Now:yyyyMMdd-HHmmss}-p{Environment.ProcessId}.log");
        CurrentLogPath = _filePath;

        _enabled = enabled ? 1 : 0;
        _minimumLevel = (int)minimumLevel;
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public static string CurrentLogPath { get; private set; } = string.Empty;

    public ILogger CreateLogger(string categoryName) => new SessionFileLogger(this, categoryName);

    public void Configure(bool enabled, LogLevel minimumLevel)
    {
        ValidateLevel(minimumLevel);
        Volatile.Write(ref _minimumLevel, (int)minimumLevel);
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
    }

    internal void WriteDirect(
        string category,
        string eventName,
        string message,
        LogLevel level = LogLevel.Information)
        => Write(category, level, new EventId(4100, eventName), message, null);

    public void Dispose()
    {
        _lines.Writer.TryComplete();
        try { _writerTask.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
    }

    private bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None
           && Volatile.Read(ref _enabled) != 0
           && (int)logLevel >= Volatile.Read(ref _minimumLevel);

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        if (!IsEnabled(level) || !category.StartsWith("CSharpGit", StringComparison.Ordinal)) return;

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
        FileStream? stream = null;
        StreamWriter? writer = null;
        try
        {
            await foreach (var line in _lines.Reader.ReadAllAsync())
            {
                if (writer is null)
                {
                    Directory.CreateDirectory(_directory);
                    DeleteOldLogs(_directory, keep: 19);
                    stream = new FileStream(
                        _filePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        bufferSize: 16 * 1024,
                        useAsync: true);
                    writer = new StreamWriter(stream) { AutoFlush = true };
                }

                await writer.WriteLineAsync(line);
            }
        }
        finally
        {
            if (writer is not null)
                await writer.DisposeAsync();
            else if (stream is not null)
                await stream.DisposeAsync();
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

    private static void ValidateLevel(LogLevel level)
    {
        if (level == LogLevel.None || !Enum.IsDefined(typeof(LogLevel), level))
            throw new ArgumentOutOfRangeException(nameof(level));
    }

    private sealed class SessionFileLogger(SessionFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

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
