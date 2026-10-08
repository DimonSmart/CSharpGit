using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IRepositoryHistoryRewriteContext
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    Task<bool> RunHistoryRewriteMutationAsync(
        Repository expectedRepository, Func<Task> mutation, string errorContext);
}

public enum PathRemovalPreparationStatus
{
    Ready, ToolUnavailable, NothingToRemove, Failed, RepositoryChanged, Cancelled
}

public sealed record PathRemovalPreparationResult(
    PathRemovalPreparationStatus Status,
    PathRemovalAnalysis? Analysis = null,
    string? FailureMessage = null);

public enum PathRemovalExecutionStatus
{
    Completed, RewriteCompletedButRefreshFailed, Failed, Cancelled, RepositoryChanged
}

public sealed record PathRemovalExecutionResult(
    PathRemovalExecutionStatus Status,
    PathRemovalResult? Result = null,
    string? FailureMessage = null,
    string? BackupPath = null,
    bool DestructivePhaseStarted = false,
    HistoryRewriteFailureKind? FailureKind = null);

public sealed class RepositoryHistoryRewriteViewModel
{
    private readonly IRepositoryHistoryRewriteService _service;
    private IRepositoryHistoryRewriteContext? _context;
    private int _inProgress;

    public RepositoryHistoryRewriteViewModel(IRepositoryHistoryRewriteService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    internal void Attach(IRepositoryHistoryRewriteContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public bool IsInProgress => Volatile.Read(ref _inProgress) != 0;

    private bool CanExecute(Repository repository) =>
        _context is { IsBusy: false } context
        && ReferenceEquals(context.Repository, repository);

    public async Task<PathRemovalPreparationResult> PreparePathRemovalAsync(
        Repository repository, string path, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(repository) || IsInProgress)
            return new(PathRemovalPreparationStatus.RepositoryChanged);
        try
        {
            var status = await _service.GetToolStatusAsync(repository, cancellationToken);
            if (!CanExecute(repository))
                return new(PathRemovalPreparationStatus.RepositoryChanged);
            if (!status.IsAvailable)
                return new(PathRemovalPreparationStatus.ToolUnavailable);
            var analysis = await _service.AnalyzePathRemovalAsync(repository, path, cancellationToken);
            if (!CanExecute(repository))
                return new(PathRemovalPreparationStatus.RepositoryChanged);
            return analysis.PathHistoryCommitCount == 0
                ? new(PathRemovalPreparationStatus.NothingToRemove)
                : new(PathRemovalPreparationStatus.Ready, analysis);
        }
        catch (OperationCanceledException) { return new(PathRemovalPreparationStatus.Cancelled); }
        catch (Exception ex) { return new(PathRemovalPreparationStatus.Failed, FailureMessage: ex.Message); }
    }

    public async Task<PathRemovalExecutionResult> RemovePathAsync(
        Repository repository, string path, Action? beforeMutation = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(repository))
            return new(PathRemovalExecutionStatus.RepositoryChanged);
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
            return new(PathRemovalExecutionStatus.Failed,
                FailureMessage: "A history rewrite is already in progress.");
        try
        {
            if (!CanExecute(repository))
                return new(PathRemovalExecutionStatus.RepositoryChanged);
            PathRemovalResult? result = null;
            Exception? failure = null;
            bool succeeded;
            try
            {
                succeeded = await _context!.RunHistoryRewriteMutationAsync(
                    repository,
                    async () =>
                    {
                        beforeMutation?.Invoke();
                        try { result = await _service.RemovePathFromHistoryAsync(repository, path, cancellationToken); }
                        catch (Exception ex) { failure = ex; throw; }
                    },
                    "Repository history rewrite did not complete successfully.");
            }
            catch (OperationCanceledException) { return new(PathRemovalExecutionStatus.Cancelled); }
            catch (Exception ex) { failure ??= ex; succeeded = false; }

            if (result is not null)
                return new(
                    succeeded ? PathRemovalExecutionStatus.Completed : PathRemovalExecutionStatus.RewriteCompletedButRefreshFailed,
                    result, BackupPath: result.BackupPath);
            if (failure is RepositoryHistoryRewriteException rewriteFailure)
                return new(PathRemovalExecutionStatus.Failed,
                    FailureMessage: rewriteFailure.Message, BackupPath: rewriteFailure.BackupPath,
                    DestructivePhaseStarted: rewriteFailure.DestructivePhaseStarted,
                    FailureKind: rewriteFailure.Kind);
            return new(PathRemovalExecutionStatus.Failed, FailureMessage: failure?.Message);
        }
        finally { Volatile.Write(ref _inProgress, 0); }
    }
}
