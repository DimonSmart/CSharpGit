using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface ICommitActionService
{
    Task<ApplyCommitResult> CherryPickAsync(
        Repository repository,
        string commit,
        int? mainlineParent = null,
        CancellationToken cancellationToken = default);
    Task<ApplyCommitResult> RevertAsync(
        Repository repository,
        string commit,
        int? mainlineParent = null,
        CancellationToken cancellationToken = default);
    Task ResetAsync(
        Repository repository,
        string commit,
        ResetMode mode,
        CancellationToken cancellationToken = default);
    Task<EditCommitMessageResult> EditCommitMessageAsync(
        Repository repository,
        string commit,
        string newMessage,
        CancellationToken cancellationToken = default);
}
