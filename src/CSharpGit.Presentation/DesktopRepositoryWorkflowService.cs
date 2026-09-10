using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Presentation;

public sealed class DesktopRepositoryWorkflowService(
    GitCliRepositoryService inner,
    IDesktopShellService shellService,
    IRepositoryPathService pathService) : IRepositoryWorkflowService
{
    public Task CreateStashAsync(Repository repository, string? message = null, CancellationToken cancellationToken = default) =>
        inner.CreateStashAsync(repository, message, cancellationToken);

    public Task ApplyStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default) =>
        inner.ApplyStashAsync(repository, stashName, cancellationToken);

    public Task PopStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default) =>
        inner.PopStashAsync(repository, stashName, cancellationToken);

    public Task<MergeResult> MergeAsync(Repository repository, string branch, CancellationToken cancellationToken = default) =>
        inner.MergeAsync(repository, branch, cancellationToken);

    public Task<InteractiveRebasePlan> ReadInteractiveRebasePlanAsync(Repository repository, string onto, CancellationToken cancellationToken = default) =>
        inner.ReadInteractiveRebasePlanAsync(repository, onto, cancellationToken);

    public Task<RebaseResult> StartInteractiveRebaseAsync(Repository repository, InteractiveRebasePlan plan, CancellationToken cancellationToken = default) =>
        inner.StartInteractiveRebaseAsync(repository, plan, cancellationToken);

    public Task<RebaseResult> ContinueRebaseAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.ContinueRebaseAsync(repository, cancellationToken);

    public Task AbortRebaseAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.AbortRebaseAsync(repository, cancellationToken);

    public Task ChooseConflictSideAsync(Repository repository, ConflictFile conflict, ConflictResolutionSide side, CancellationToken cancellationToken = default) =>
        inner.ChooseConflictSideAsync(repository, conflict, side, cancellationToken);

    public Task KeepConflictDeletionAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default) =>
        inner.KeepConflictDeletionAsync(repository, conflict, cancellationToken);

    public Task StageResolvedConflictAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default) =>
        inner.StageResolvedConflictAsync(repository, conflict, cancellationToken);

    public Task ConfigureMergeToolAsync(Repository repository, MergeToolConfiguration configuration, CancellationToken cancellationToken = default) =>
        inner.ConfigureMergeToolAsync(repository, configuration, cancellationToken);

    public Task RunMergeToolForFileAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default) =>
        inner.RunMergeToolForFileAsync(repository, conflict, cancellationToken);

    public Task RunMergeToolWorkflowAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.RunMergeToolWorkflowAsync(repository, cancellationToken);

    public Task ContinueOperationAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.ContinueOperationAsync(repository, cancellationToken);

    public Task AbortOperationAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.AbortOperationAsync(repository, cancellationToken);

    public Task SkipOperationAsync(Repository repository, CancellationToken cancellationToken = default) =>
        inner.SkipOperationAsync(repository, cancellationToken);

    public async Task OpenConflictAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(conflict);
        if (!conflict.CanOpenManually)
            throw new InvalidOperationException("This conflict cannot be opened as a working-copy file.");

        var path = pathService.ResolveExistingWorkingTreeFile(repository, conflict.Path);
        await shellService.OpenFileAsync(path, cancellationToken);
    }
}
