using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IExternalGitToolService
{
    Task OpenEditorAsync(
        Repository? repository,
        string filePath,
        CancellationToken cancellationToken = default);

    Task RunExternalDiffAsync(
        Repository repository,
        DiffFileVersionPair pair,
        CancellationToken cancellationToken = default);

    Task RunMergeToolForFileAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default);

    Task RunMergeToolWorkflowAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task OpenConflictInEditorAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default);

    Task TestAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken = default);
}
