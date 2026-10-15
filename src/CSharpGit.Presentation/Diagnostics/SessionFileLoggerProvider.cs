using System.Globalization;
using CSharpGit.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.Diagnostics;

internal sealed class SessionFileLoggerProvider : ILoggerProvider
{
    // Approximately 8 MiB maximum retained queue payload, plus object overhead.
    private readonly BoundedDiagnosticQueue _queue = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _directory;
    private readonly string _filePath;
    private readonly Task _writerTask;
    private int _enabled;
    private int _minimumLevel;
    private int _disposed;

    internal long DroppedEntries => _queue.TotalDropped;
    internal bool HasFailed => _queue.HasFailed;

    public SessionFileLoggerProvider(
        bool enabled = false,
        LogLevel minimumLevel = LogLevel.Information)
    {
        ValidateLevel(minimumLevel);

        _directory = Path.Combine(
            ApplicationDataPaths.Root,
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Complete();
        try
        {
            if (!_writerTask.Wait(TimeSpan.FromSeconds(2)))
                _shutdown.Cancel();
        }
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
        Enqueue($"{timestamp} [{level}] [tid={Environment.CurrentManagedThreadId}] [{category}]{eventText} {message}");

        if (exception is not null)
            Enqueue(exception.ToString());
    }

    private void Enqueue(string line)
    {
        _queue.TryWrite(line);
    }

    private async Task WriteLoopAsync()
    {
        FileStream? stream = null;
        StreamWriter? writer = null;
        try
        {
            await foreach (var line in _queue.Reader.ReadAllAsync(_shutdown.Token))
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

                var dropped = _queue.TakePendingDropped();
                if (dropped > 0)
                {
                    try { await writer.WriteLineAsync($"{dropped} log entries dropped".AsMemory(), _shutdown.Token); }
                    catch
                    {
                        _queue.RestorePendingDropped(dropped);
                        throw;
                    }
                }
                await writer.WriteLineAsync(line.AsMemory(), _shutdown.Token);
            }

            if (writer is not null)
            {
                var dropped = _queue.TakePendingDropped();
                if (dropped > 0)
                    await writer.WriteLineAsync($"{dropped} log entries dropped".AsMemory(), _shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _queue.Fail();
            System.Diagnostics.Trace.WriteLine("CSharpGit file logger stopped after a write failure.");
        }
        finally
        {
            try
            {
                if (writer is not null)
                    await writer.DisposeAsync();
                else if (stream is not null)
                    await stream.DisposeAsync();
            }
            catch (IOException)
            {
                _queue.Fail();
            }
            catch (UnauthorizedAccessException)
            {
                _queue.Fail();
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
