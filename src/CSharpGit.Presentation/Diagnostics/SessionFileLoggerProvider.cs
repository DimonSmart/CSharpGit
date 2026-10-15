using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.Diagnostics;

internal sealed class SessionFileLoggerProvider : ILoggerProvider
{
    // Maximum retained payload is approximately 512 * 16 KiB = 8 MiB (plus object overhead).
    internal const int MaximumQueuedEntries = 512;
    internal const int MaximumEntryBytes = 16 * 1024;
    private readonly Channel<string> _lines = Channel.CreateBounded<string>(new BoundedChannelOptions(MaximumQueuedEntries)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _directory;
    private readonly string _filePath;
    private readonly Task _writerTask;
    private int _enabled;
    private int _minimumLevel;
    private long _droppedEntries;
    private int _disposed;
    private int _failed;

    internal long DroppedEntries => Interlocked.Read(ref _droppedEntries);
    internal bool HasFailed => Volatile.Read(ref _failed) != 0;

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lines.Writer.TryComplete();
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
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _failed) != 0
            || !_lines.Writer.TryWrite(LimitEntry(line)))
            Interlocked.Increment(ref _droppedEntries);
    }

    private static string LimitEntry(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= MaximumEntryBytes) return text;
        const string marker = " [log entry truncated]";
        var budget = MaximumEntryBytes - Encoding.UTF8.GetByteCount(marker);
        var bytes = 0;
        var end = 0;
        while (end < text.Length)
        {
            if (!Rune.TryGetRuneAt(text, end, out var rune))
                rune = Rune.ReplacementChar;
            if (bytes + rune.Utf8SequenceLength > budget) break;
            bytes += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }
        return string.Concat(text.AsSpan(0, end), marker);
    }

    private async Task WriteLoopAsync()
    {
        FileStream? stream = null;
        StreamWriter? writer = null;
        try
        {
            await foreach (var line in _lines.Reader.ReadAllAsync(_shutdown.Token))
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

                var dropped = Interlocked.Exchange(ref _droppedEntries, 0);
                if (dropped > 0)
                {
                    try { await writer.WriteLineAsync($"{dropped} log entries dropped".AsMemory(), _shutdown.Token); }
                    catch
                    {
                        Interlocked.Add(ref _droppedEntries, dropped);
                        throw;
                    }
                }
                await writer.WriteLineAsync(line.AsMemory(), _shutdown.Token);
            }

            if (writer is not null)
            {
                var dropped = Interlocked.Exchange(ref _droppedEntries, 0);
                if (dropped > 0)
                    await writer.WriteLineAsync($"{dropped} log entries dropped".AsMemory(), _shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Volatile.Write(ref _failed, 1);
            _lines.Writer.TryComplete();
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
                Volatile.Write(ref _failed, 1);
            }
            catch (UnauthorizedAccessException)
            {
                Volatile.Write(ref _failed, 1);
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
