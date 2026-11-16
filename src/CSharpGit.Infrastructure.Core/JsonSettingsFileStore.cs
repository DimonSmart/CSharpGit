using System.Diagnostics;

namespace CSharpGit.Infrastructure;

// One lock file per settings path coordinates independent app processes.
// Lock files are intentionally not deleted: deleting them would create a race
// between a waiter and a new writer using a different file identity.
internal sealed class JsonSettingsFileStore
{
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan LockWaitLimit = TimeSpan.FromSeconds(15);
    private readonly string _filePath;

    internal JsonSettingsFileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    internal string? ReadText()
    {
        try
        {
            return File.ReadAllText(_filePath);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal async Task<FileStream> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var lockPath = _filePath + ".lock";
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // FileShare.None is enforced by .NET across cooperating processes.
                return new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (
                File.Exists(lockPath) &&
                Stopwatch.GetElapsedTime(started) < LockWaitLimit)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Must be called only while holding AcquireWriteLockAsync's lease.
    internal async Task WriteAtomicAsync(string json, CancellationToken cancellationToken)
    {
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
