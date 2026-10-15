using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Presentation.Diagnostics;

// Available only in the explicitly enabled desktop integration test host.
internal sealed class DesktopLifecycleStateFaults(GitRepositoryStateService inner)
    : IRepositoryStateService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<string>> _failures =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public void FailNextSnapshot(string workingDirectory, string reason)
    {
        var path = Path.GetFullPath(workingDirectory);
        lock (_gate)
        {
            if (!_failures.TryGetValue(path, out var reasons))
                _failures.Add(path, reasons = new Queue<string>());
            reasons.Enqueue(reason);
        }
    }

    public Task<RepositoryState> ReadAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(repository, cancellationToken);

    public Task<RepositoryState> ReadLocalOnlyAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.ReadLocalOnlyAsync(repository, cancellationToken);

    public Task<RepositoryStateReadResult> ReadWithWorkingTreeStatusAsync(
        Repository repository, bool localOnly = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? reason = null;
        lock (_gate)
        {
            if (_failures.TryGetValue(Path.GetFullPath(repository.WorkingDirectory), out var queue) && queue.Count > 0)
                reason = queue.Dequeue();
        }
        if (reason is not null) throw new IOException(reason);
        return inner.ReadWithWorkingTreeStatusAsync(repository, localOnly, cancellationToken);
    }
}
