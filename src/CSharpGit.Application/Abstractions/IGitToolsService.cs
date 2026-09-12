using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IGitToolsService
{
    Task<GitToolConfigurationSnapshot> ReadAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Repository? repository,
        GitToolEdit edit,
        CancellationToken cancellationToken = default);

    Task RemoveOverrideAsync(
        Repository? repository,
        GitToolKind kind,
        GitToolWriteScope scope,
        CancellationToken cancellationToken = default);

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

    Task TestAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken = default);
}

public interface IExternalToolProcessService
{
    string? ResolveExecutable(string commandOrPath);

    bool IsExecutablePathUsable(string path);

    Task RunShellCommandAsync(
        string rawCommand,
        string fileArgument,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
