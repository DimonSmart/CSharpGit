using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation;

internal sealed class RecentRepositoryFolderPicker(IFolderPicker inner) : IFolderPicker
{
    private readonly object _gate = new();
    private string? _queuedPath;

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_queuedPath is { } queuedPath)
            {
                _queuedPath = null;
                return Task.FromResult<string?>(queuedPath);
            }
        }

        return inner.PickFolderAsync(cancellationToken);
    }

    internal void QueuePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate) _queuedPath = path;
    }
}
