using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

/// <summary>
/// Repository-session owner admission for new user-initiated Git workflows.
/// A successful call reserves the common busy/mutation gate until the lease is disposed.
/// Internal continuations of already admitted workflows do not acquire a second lease.
/// </summary>
public interface IUserGitOperationAdmission
{
    bool CanStartUserGitOperation { get; }
    IDisposable? TryBeginUserGitOperation(Repository expectedRepository);
}
