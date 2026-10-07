using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class CommitActionsViewModelTests
{
    [Fact]
    public void AvailabilityTracksRepositoryBusyOperationBranchAndCommitShape()
    {
        var repository = Repository("repo");
        var context = new FakeContext();
        var viewModel = CreateViewModel(context, out _, out _);
        var normal = Commit("commit", "parent");
        var merge = Commit("merge", "parent-1", "parent-2");

        Assert.False(viewModel.CanMutateCommit(normal));

        context.Repository = repository;
        context.CurrentBranchName = "main";
        Assert.True(viewModel.CanCheckout(normal));
        Assert.True(viewModel.CanCherryPick(normal));
        Assert.True(viewModel.CanRevert(normal));
        Assert.True(viewModel.CanReset(normal));
        Assert.True(viewModel.CanFixup(normal));
        Assert.False(viewModel.CanFixup(merge));

        context.CurrentBranchName = null;
        Assert.False(viewModel.CanReset(normal));
        Assert.False(viewModel.CanFixup(normal));

        context.CurrentBranchName = "main";
        context.IsBusy = true;
        Assert.False(viewModel.CanMutateCommit(normal));

        context.IsBusy = false;
        context.CurrentOperation = RepositoryOperation.Rebase;
        Assert.False(viewModel.CanMutateCommit(normal));
    }

    [Fact]
    public async Task CheckoutUsesExpectedRepositoryAndMutationLifecycle()
    {
        var repository = Repository("repo");
        var context = new FakeContext
        {
            Repository = repository,
            CurrentBranchName = "main"
        };
        var viewModel = CreateViewModel(context, out var references, out _);
        var commit = Commit("abc", "parent");

        var result = await viewModel.CheckoutAsync(repository, commit);

        Assert.True(result.Succeeded);
        Assert.Equal("abc", result.SelectionCommit);
        Assert.Equal(["checkout:abc"], references.Calls);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task StaleRepositoryDoesNotStartMutation()
    {
        var first = Repository("first");
        var second = Repository("second");
        var context = new FakeContext
        {
            Repository = second,
            CurrentBranchName = "main"
        };
        var viewModel = CreateViewModel(context, out var references, out var actions);

        var result = await viewModel.CheckoutAsync(first, Commit("abc", "parent"));

        Assert.False(result.Succeeded);
        Assert.Empty(references.Calls);
        Assert.Empty(actions.Calls);
        Assert.Equal(0, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task CherryPickAndRevertPreserveMainlineAndApplyResultSemantics()
    {
        var repository = Repository("repo");
        var context = new FakeContext
        {
            Repository = repository,
            CurrentBranchName = "main"
        };
        var viewModel = CreateViewModel(context, out _, out var actions);
        var merge = Commit("merge", "parent-1", "parent-2");

        var missingMainline = await viewModel.CherryPickAsync(repository, merge, null);
        Assert.False(missingMainline.Succeeded);
        Assert.Empty(actions.Calls);

        actions.CherryPickResult = new ApplyCommitResult(
            ApplyCommitResultKind.Completed,
            "done",
            "new-head");
        var cherryPick = await viewModel.CherryPickAsync(repository, merge, 2);

        Assert.True(cherryPick.Succeeded);
        Assert.Equal("new-head", cherryPick.SelectionCommit);
        Assert.Contains("cherry-pick:merge:2", actions.Calls);

        actions.RevertResult = new ApplyCommitResult(
            ApplyCommitResultKind.Conflicts,
            "conflicts");
        var revertConflict = await viewModel.RevertAsync(repository, merge, 1);

        Assert.True(revertConflict.Succeeded);
        Assert.Equal("merge", revertConflict.SelectionCommit);
        Assert.Contains("revert:merge:1", actions.Calls);

        actions.RevertResult = new ApplyCommitResult(
            ApplyCommitResultKind.Failed,
            "revert failed");
        var revertFailed = await viewModel.RevertAsync(repository, merge, 1);

        Assert.True(revertFailed.LifecycleSucceeded);
        Assert.False(revertFailed.Succeeded);
        Assert.Equal("Revert failed", revertFailed.ErrorTitle);
        Assert.Equal("revert failed", revertFailed.ErrorMessage);
    }

    [Theory]
    [InlineData(ResetMode.Soft)]
    [InlineData(ResetMode.Mixed)]
    [InlineData(ResetMode.Hard)]
    public async Task ResetPassesRequestedMode(ResetMode mode)
    {
        var repository = Repository("repo");
        var context = new FakeContext
        {
            Repository = repository,
            CurrentBranchName = "main"
        };
        var viewModel = CreateViewModel(context, out _, out var actions);
        var commit = Commit("target", "parent");

        var result = await viewModel.ResetAsync(repository, commit, mode);

        Assert.True(result.Succeeded);
        Assert.Equal("target", result.SelectionCommit);
        Assert.Contains($"reset:target:{mode}", actions.Calls);
    }

    [Fact]
    public async Task FixupUsesHistoryRewriteLifecycleAndPreservesFailureMessage()
    {
        var repository = Repository("repo");
        var context = new FakeContext
        {
            Repository = repository,
            CurrentBranchName = "main"
        };
        var viewModel = CreateViewModel(context, out _, out var actions);
        actions.FixupResult = new RebaseResult(RebaseResultKind.Failed, "fixup failed");

        var result = await viewModel.FixupAsync(
            repository,
            Commit("target", "parent"));

        Assert.False(result.Succeeded);
        Assert.Equal(1, context.HistoryRewriteLifecycleCalls);
        Assert.Equal("fixup failed", context.LastFailure);
        Assert.Contains("fixup:target", actions.Calls);
    }

    private static CommitActionsViewModel CreateViewModel(
        FakeContext context,
        out FakeReferenceService references,
        out FakeCommitActionService actions)
    {
        references = new FakeReferenceService();
        actions = new FakeCommitActionService();
        var viewModel = new CommitActionsViewModel(actions, references);
        viewModel.Attach(context);
        return viewModel;
    }

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private static CommitHistoryItem Commit(string hash, params string[] parents) =>
        new(
            hash,
            parents,
            hash,
            hash,
            "author",
            DateTimeOffset.UnixEpoch,
            []);

    private sealed class FakeContext : ICommitActionsRepositoryContext
    {
        public Repository? Repository { get; set; }
        public bool IsBusy { get; set; }
        public RepositoryOperation CurrentOperation { get; set; }
        public string? CurrentBranchName { get; set; }
        public int MutationLifecycleCalls { get; private set; }
        public int HistoryRewriteLifecycleCalls { get; private set; }
        public string? LastFailure { get; private set; }

        public Task<bool> RunCommitMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string errorContext) =>
            RunAsync(expectedRepository, mutation, historyRewrite: false);

        public Task<bool> RunCommitHistoryRewriteMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string errorContext) =>
            RunAsync(expectedRepository, mutation, historyRewrite: true);

        private async Task<bool> RunAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            bool historyRewrite)
        {
            if (!ReferenceEquals(Repository, expectedRepository)
                || IsBusy
                || CurrentOperation != RepositoryOperation.None)
            {
                return false;
            }

            if (historyRewrite)
                HistoryRewriteLifecycleCalls++;
            else
                MutationLifecycleCalls++;

            try
            {
                await mutation();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LastFailure = exception.Message;
                return false;
            }
        }
    }

    private sealed class FakeCommitActionService : ICommitActionService
    {
        public List<string> Calls { get; } = [];
        public ApplyCommitResult CherryPickResult { get; set; } =
            new(ApplyCommitResultKind.Completed, "done", "head");
        public ApplyCommitResult RevertResult { get; set; } =
            new(ApplyCommitResultKind.Completed, "done", "head");
        public RebaseResult FixupResult { get; set; } =
            new(RebaseResultKind.Completed, "done");

        public Task<ApplyCommitResult> CherryPickAsync(
            Repository repository,
            string commit,
            int? mainlineParent = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"cherry-pick:{commit}:{mainlineParent}");
            return Task.FromResult(CherryPickResult);
        }

        public Task<ApplyCommitResult> RevertAsync(
            Repository repository,
            string commit,
            int? mainlineParent = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"revert:{commit}:{mainlineParent}");
            return Task.FromResult(RevertResult);
        }

        public Task ResetAsync(
            Repository repository,
            string commit,
            ResetMode mode,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"reset:{commit}:{mode}");
            return Task.CompletedTask;
        }

        public Task<RebaseResult> FixupIntoPreviousCommitAsync(
            Repository repository,
            string commit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"fixup:{commit}");
            return Task.FromResult(FixupResult);
        }

        public Task<EditCommitMessageResult> EditCommitMessageAsync(
            Repository repository,
            string commit,
            string newMessage,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EditCommitMessageResult(
                EditCommitMessageResultKind.Completed,
                "done",
                commit,
                commit));
    }

    private sealed class FakeReferenceService : IReferenceService
    {
        public List<string> Calls { get; } = [];

        public Task SwitchBranchAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CheckoutAsync(
            Repository repository,
            string reference,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"checkout:{reference}");
            return Task.CompletedTask;
        }

        public Task CreateBranchAsync(
            Repository repository,
            string branch,
            string? startPoint = null,
            bool switchToBranch = true,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RenameBranchAsync(
            Repository repository,
            string oldName,
            string newName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteBranchAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteBranchAsync(
            Repository repository,
            string branch,
            BranchDeletionMode mode,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CheckoutRemoteBranchAsync(
            Repository repository,
            string remoteBranch,
            string localBranch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
