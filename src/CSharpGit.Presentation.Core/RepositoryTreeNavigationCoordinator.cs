using CSharpGit.Domain;

namespace CSharpGit.Presentation;

/// <summary>
/// Maintains the current repository-tree navigation intent and defers requests
/// while a Git operation owns the repository. No XAML or Git execution lives here.
/// </summary>
internal sealed class RepositoryTreeNavigationCoordinator
{
    private long _version;
    private PendingReference? _pendingReference;
    private PendingCheckout? _pendingCheckout;

    internal sealed record PendingReference(
        Repository Repository,
        long Version,
        long SelectionIntentVersion,
        string CommitHash,
        bool PreserveSelectedStash);

    internal sealed record PendingCheckout(
        Repository Repository,
        long Version,
        string BranchName);

    public long Version => _version;
    public bool HasPendingReference => _pendingReference is not null;
    public bool HasPendingCheckout => _pendingCheckout is not null;

    public void ResetForRepositoryChange() => BeginInteraction();

    public long BeginInteraction()
    {
        _pendingReference = null;
        _pendingCheckout = null;
        return ++_version;
    }

    public void DeferReference(
        Repository repository,
        long version,
        long selectionIntentVersion,
        string commitHash,
        bool preserveSelectedStash)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (version != _version) return;

        _pendingReference = new PendingReference(
            repository, version, selectionIntentVersion, commitHash, preserveSelectedStash);
    }

    public void DeferCheckout(Repository repository, long version, string branchName)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (version != _version) return;

        _pendingCheckout = new PendingCheckout(repository, version, branchName);
    }

    public bool TryTakeReference(
        Repository? currentRepository,
        long currentSelectionIntentVersion,
        out PendingReference pending)
    {
        var candidate = _pendingReference;
        _pendingReference = null;
        if (candidate is null
            || candidate.Version != _version
            || !ReferenceEquals(candidate.Repository, currentRepository)
            || candidate.SelectionIntentVersion != currentSelectionIntentVersion)
        {
            pending = null!;
            return false;
        }

        pending = candidate;
        return true;
    }

    public bool TryTakeCheckout(Repository? currentRepository, out PendingCheckout pending)
    {
        var candidate = _pendingCheckout;
        _pendingCheckout = null;
        if (candidate is null
            || candidate.Version != _version
            || !ReferenceEquals(candidate.Repository, currentRepository))
        {
            pending = null!;
            return false;
        }

        pending = candidate;
        return true;
    }
}
