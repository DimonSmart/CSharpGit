using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface ICommitActionsRepositoryContext
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    RepositoryOperation CurrentOperation { get; }
    string? CurrentBranchName { get; }

    Task<bool> RunCommitMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext);

    Task<bool> RunCommitHistoryRewriteMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext);
}

public sealed record CommitActionExecutionResult(
    bool LifecycleSucceeded,
    string? SelectionCommit = null,
    string? ErrorTitle = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => LifecycleSucceeded && string.IsNullOrWhiteSpace(ErrorMessage);
}

public sealed class CommitActionsViewModel
{
    private readonly ICommitActionService _commitActionService;
    private readonly IReferenceService _referenceService;
    private ICommitActionsRepositoryContext? _context;

    public CommitActionsViewModel(
        ICommitActionService commitActionService,
        IReferenceService referenceService)
    {
        _commitActionService = commitActionService ?? throw new ArgumentNullException(nameof(commitActionService));
        _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
    }

    internal void Attach(ICommitActionsRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public bool CanMutateCommit(CommitHistoryItem? commit) =>
        commit is not null
        && _context?.Repository is not null
        && !_context.IsBusy
        && _context.CurrentOperation == RepositoryOperation.None;

    public bool CanCheckout(CommitHistoryItem? commit) => CanMutateCommit(commit);

    public bool CanCherryPick(CommitHistoryItem? commit) => CanMutateCommit(commit);

    public bool CanRevert(CommitHistoryItem? commit) => CanMutateCommit(commit);

    public bool CanReset(CommitHistoryItem? commit) =>
        CanMutateCommit(commit)
        && _context?.CurrentBranchName is not null;

    public bool CanFixup(CommitHistoryItem? commit) =>
        CanReset(commit)
        && commit?.Parents.Count == 1;

    public async Task<CommitActionExecutionResult> CheckoutAsync(
        Repository repository,
        CommitHistoryItem commit)
    {
        if (!CanExecute(repository, commit))
            return new(false);

        var succeeded = await _context!.RunCommitMutationAsync(
            repository,
            () => _referenceService.CheckoutAsync(repository, commit.Hash),
            "Could not checkout commit");

        return new(
            succeeded,
            SelectionCommit: succeeded ? commit.Hash : null);
    }

    public async Task<CommitActionExecutionResult> CherryPickAsync(
        Repository repository,
        CommitHistoryItem commit,
        int? mainline)
    {
        if (!CanExecute(repository, commit)
            || commit.Parents.Count > 1 && mainline is null)
        {
            return new(false);
        }

        ApplyCommitResult? result = null;
        var lifecycleSucceeded = await _context!.RunCommitMutationAsync(
            repository,
            async () => result = await _commitActionService.CherryPickAsync(
                repository,
                commit.Hash,
                mainline),
            "Could not cherry-pick commit");

        return MapApplyResult(
            lifecycleSucceeded,
            result,
            commit.Hash,
            "Cherry-pick failed");
    }

    public async Task<CommitActionExecutionResult> RevertAsync(
        Repository repository,
        CommitHistoryItem commit,
        int? mainline)
    {
        if (!CanExecute(repository, commit)
            || commit.Parents.Count > 1 && mainline is null)
        {
            return new(false);
        }

        ApplyCommitResult? result = null;
        var lifecycleSucceeded = await _context!.RunCommitMutationAsync(
            repository,
            async () => result = await _commitActionService.RevertAsync(
                repository,
                commit.Hash,
                mainline),
            "Could not revert commit");

        return MapApplyResult(
            lifecycleSucceeded,
            result,
            commit.Hash,
            "Revert failed");
    }

    public async Task<CommitActionExecutionResult> ResetAsync(
        Repository repository,
        CommitHistoryItem commit,
        ResetMode mode)
    {
        if (!CanExecute(repository, commit, requireLocalBranch: true))
            return new(false);

        var succeeded = await _context!.RunCommitMutationAsync(
            repository,
            () => _commitActionService.ResetAsync(repository, commit.Hash, mode),
            $"Could not {mode.ToString().ToLowerInvariant()} reset");

        return new(
            succeeded,
            SelectionCommit: succeeded ? commit.Hash : null);
    }

    public async Task<CommitActionExecutionResult> FixupAsync(
        Repository repository,
        CommitHistoryItem commit)
    {
        if (!CanExecute(repository, commit, requireLocalBranch: true)
            || commit.Parents.Count != 1)
        {
            return new(false);
        }

        RebaseResult? result = null;
        var succeeded = await _context!.RunCommitHistoryRewriteMutationAsync(
            repository,
            async () =>
            {
                result = await _commitActionService.FixupIntoPreviousCommitAsync(
                    repository,
                    commit.Hash);
                if (result.Kind == RebaseResultKind.Failed)
                    throw new InvalidOperationException(result.Message);
            },
            "Could not fixup commit");

        return new(succeeded);
    }

    private bool CanExecute(
        Repository repository,
        CommitHistoryItem commit,
        bool requireLocalBranch = false)
    {
        var context = _context;
        return context is not null
               && ReferenceEquals(context.Repository, repository)
               && !context.IsBusy
               && context.CurrentOperation == RepositoryOperation.None
               && (!requireLocalBranch || context.CurrentBranchName is not null)
               && commit is not null;
    }

    private static CommitActionExecutionResult MapApplyResult(
        bool lifecycleSucceeded,
        ApplyCommitResult? result,
        string originalCommit,
        string errorTitle)
    {
        if (!lifecycleSucceeded || result is null)
            return new(false);

        return result.Kind switch
        {
            ApplyCommitResultKind.Completed => new(
                true,
                SelectionCommit: result.HeadCommit),
            ApplyCommitResultKind.Conflicts => new(
                true,
                SelectionCommit: originalCommit),
            ApplyCommitResultKind.Failed => new(
                true,
                ErrorTitle: errorTitle,
                ErrorMessage: result.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Kind))
        };
    }
}
