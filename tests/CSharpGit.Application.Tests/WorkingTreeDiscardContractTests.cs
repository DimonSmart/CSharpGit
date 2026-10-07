using CSharpGit.Domain;

namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeDiscardContractTests
{
    [Fact]
    public void ConfirmationReportsAffectedAndUntrackedCounts()
    {
        var request = WorkingTreeDiscard.CreateSelected(
        [
            new WorkingTreeChange("one.cs", ' ', 'M'),
            new WorkingTreeChange("two.cs", ' ', 'D'),
            new WorkingTreeChange("one.tmp", '?', '?'),
            new WorkingTreeChange("two.tmp", '?', '?')
        ]);

        Assert.NotNull(request);
        Assert.Equal(4, request.Changes.Count);
        Assert.Equal(2, request.UntrackedCount);
        Assert.Equal(
            $"Discard changes in 4 selected files?{Environment.NewLine}2 untracked files will be permanently deleted.",
            request.ConfirmationMessage);
    }

    [Fact]
    public void SingleSelectedConfirmationNamesTheExactFile()
    {
        var request = WorkingTreeDiscard.CreateSelected(
        [
            new WorkingTreeChange("src/Foo.cs", ' ', 'M')
        ]);

        Assert.NotNull(request);
        Assert.Equal("Discard unstaged changes in 'src/Foo.cs'?", request.ConfirmationMessage);
    }

    [Fact]
    public void ConfirmationOmitsPermanentDeletionWarningWithoutUntrackedFiles()
    {
        var request = WorkingTreeDiscard.CreateAll(
        [
            new WorkingTreeChange("one.cs", ' ', 'M'),
            new WorkingTreeChange("two.cs", ' ', 'D')
        ]);

        Assert.NotNull(request);
        Assert.Equal("Discard changes in 2 files?", request.ConfirmationMessage);
        Assert.DoesNotContain("permanently deleted", request.ConfirmationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscardAllRequestIsAnImmutableSnapshotOfEligibleItems()
    {
        var changes = new List<WorkingTreeChange>
        {
            new("tracked.cs", ' ', 'M'),
            new("untracked.tmp", '?', '?'),
            new("conflict.cs", 'U', 'U')
        };

        var request = WorkingTreeDiscard.CreateAll(changes);
        changes.Add(new WorkingTreeChange("later.tmp", '?', '?'));
        changes.Clear();

        Assert.NotNull(request);
        Assert.Equal(2, request.Changes.Count);
        Assert.Contains(request.Changes, change => change.Path == "tracked.cs");
        Assert.Contains(request.Changes, change => change.Path == "untracked.tmp");
        Assert.DoesNotContain(request.Changes, change => change.Path is "conflict.cs" or "later.tmp");
    }

    [Fact]
    public void SelectedDiscardRejectsMixedConflictSelectionInsteadOfSilentlySkippingIt()
    {
        var ordinary = new WorkingTreeChange("ordinary.cs", ' ', 'M');
        var conflict = new WorkingTreeChange("conflict.cs", 'U', 'U');

        Assert.True(WorkingTreeDiscard.CanDiscardSelected([ordinary]));
        Assert.False(WorkingTreeDiscard.CanDiscardSelected([ordinary, conflict]));
        Assert.Null(WorkingTreeDiscard.CreateSelected([ordinary, conflict]));
    }

    [Fact]
    public async Task SmallTrackedSetUsesOneBatchAndPreservesResultOrder()
    {
        var changes = new[]
        {
            new WorkingTreeChange("one.cs", ' ', 'M'),
            new WorkingTreeChange("two.cs", ' ', 'D'),
            new WorkingTreeChange("three.cs", ' ', 'M')
        };
        var request = WorkingTreeDiscard.CreateSelected(changes)!;
        var batchCalls = new List<string[]>();
        var singleCalls = 0;

        var results = await WorkingTreeDiscard.ExecuteAsync(
            CreateDummyRepository(),
            request,
            (batch, _) =>
            {
                batchCalls.Add(batch.Select(change => change.Path).ToArray());
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                singleCalls++;
                return Task.CompletedTask;
            });

        var batch = Assert.Single(batchCalls);
        Assert.Equal(changes.Select(change => change.Path), batch);
        Assert.Equal(0, singleCalls);
        Assert.Equal(changes.Select(change => change.Path), results.Select(result => result.Path));
        Assert.All(results, result => Assert.Equal(WorkingTreeDiscardOutcome.Restored, result.Outcome));
    }

    [Fact]
    public async Task FailedTrackedChunkFallsBackOnlyForThatChunkAndContinues()
    {
        var changes = Enumerable.Range(0, 160)
            .Select(index => new WorkingTreeChange(
                $"{index:D3}-{new string('x', 600)}.txt",
                ' ',
                'M'))
            .ToArray();
        var request = WorkingTreeDiscard.CreateSelected(changes)!;
        var batches = new List<string[]>();
        var fallback = new List<string>();

        var results = await WorkingTreeDiscard.ExecuteAsync(
            CreateDummyRepository(),
            request,
            (batch, _) =>
            {
                batches.Add(batch.Select(change => change.Path).ToArray());
                if (batches.Count == 2)
                    throw new IOException("simulated batch failure");
                return Task.CompletedTask;
            },
            (change, _) =>
            {
                fallback.Add(change.Path);
                return Task.CompletedTask;
            });

        Assert.True(batches.Count >= 3);
        Assert.Equal(batches[1], fallback);
        Assert.Equal(changes.Select(change => change.Path), results.Select(result => result.Path));
        Assert.All(results, result => Assert.Equal(WorkingTreeDiscardOutcome.Restored, result.Outcome));
    }

    [Fact]
    public async Task CancellationBetweenChunksStopsStartingNewDestructiveOperations()
    {
        var changes = Enumerable.Range(0, 160)
            .Select(index => new WorkingTreeChange(
                $"{index:D3}-{new string('x', 600)}.txt",
                ' ',
                'M'))
            .ToArray();
        var request = WorkingTreeDiscard.CreateSelected(changes)!;
        using var cancellation = new CancellationTokenSource();
        var batchCalls = 0;
        var singleCalls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            WorkingTreeDiscard.ExecuteAsync(
                CreateDummyRepository(),
                request,
                (_, _) =>
                {
                    batchCalls++;
                    cancellation.Cancel();
                    return Task.CompletedTask;
                },
                (_, _) =>
                {
                    singleCalls++;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.Equal(1, batchCalls);
        Assert.Equal(0, singleCalls);
    }

    [Fact]
    public async Task CancellationBeforeFallbackDoesNotStartPerFileFallback()
    {
        var changes = new[]
        {
            new WorkingTreeChange("one.cs", ' ', 'M'),
            new WorkingTreeChange("two.cs", ' ', 'M')
        };
        using var cancellation = new CancellationTokenSource();
        var singleCalls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            WorkingTreeDiscard.ExecuteAsync(
                CreateDummyRepository(),
                WorkingTreeDiscard.CreateSelected(changes)!,
                (_, _) =>
                {
                    cancellation.Cancel();
                    throw new IOException("simulated batch failure");
                },
                (_, _) =>
                {
                    singleCalls++;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.Equal(0, singleCalls);
    }

    [Fact]
    public void WorkingTreeUiUsesModalDiscardConfirmation()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var dialogs = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ConfirmationDialogs.cs"));
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeViewModel.cs"));

        Assert.Contains("Command=\"{Binding WorkingTree.RequestDiscardSelectedCommand}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Discard selected\"", xaml);
        Assert.Contains("Command=\"{Binding WorkingTree.RequestDiscardAllCommand}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Discard all…\"", xaml);
        Assert.Contains("Foreground=\"{ThemeResource SystemFillColorCriticalBrush}\"", xaml);
        Assert.DoesNotContain("BatchDiscardConfirmationVisibility", xaml);
        Assert.DoesNotContain("Title=\"Discard unstaged changes?\"", xaml);

        Assert.Contains("new ContentDialog", dialogs);
        Assert.Contains("Title = \"Discard changes?\"", dialogs);
        Assert.Contains("PrimaryButtonText = \"Discard\"", dialogs);
        Assert.Contains("CloseButtonText = \"Cancel\"", dialogs);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", dialogs);
        Assert.Contains("_viewModel.WorkingTree.ConfirmBatchDiscardCommand", dialogs);
        Assert.Contains("_viewModel.WorkingTree.CancelBatchDiscardCommand", dialogs);
        Assert.Contains("BatchDiscardConfirmationMessage", dialogs);

        Assert.Contains("WorkingTreeDiscard.CreateSelected(_selectedUnstagedChanges)", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.CreateAll(Changes)", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.ExecuteAsync", discardViewModel);
        Assert.Contains("DiscardTrackedFilesAsync(repository, changes, token)", discardViewModel);
        Assert.Contains("DiscardFileAsync(repository, change, token)", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.FormatFailures(results)", discardViewModel);
        Assert.DoesNotContain("BatchDiscardConfirmationVisibility", discardViewModel);
    }

    [Fact]
    public void CancelConfirmationDoesNotRunDiscardMutation()
    {
        var root = FindRepositoryRoot();
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeViewModel.cs"));
        var method = ExtractMethod(discardViewModel, "private Task CancelBatchDiscardAsync()", "private async Task ConfirmBatchDiscardAsync()");

        Assert.Contains("SetPendingBatchDiscard(null)", method);
        Assert.DoesNotContain("MutateAsync", method);
        Assert.DoesNotContain("DiscardFileAsync", method);
        Assert.DoesNotContain("WorkingTreeDiscard.ExecuteAsync", method);
    }

    [Fact]
    public void PartialFailureStillFlowsThroughSingleMutationRefreshAndFailureReport()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeViewModel.cs"));

        var confirm = ExtractMethod(discardViewModel, "private async Task ConfirmBatchDiscardAsync()", "private void SetPendingBatchDiscard");
        Assert.Contains("RunMutationCoreAsync", confirm);
        Assert.Contains("WorkingTreeDiscard.ExecuteAsync", confirm);
        Assert.Contains("WorkingTreeDiscard.FormatFailures(results)", confirm);
        Assert.Contains("beforeMutation: ClearPresentationSelection", confirm);

        var mutate = ExtractMethod(viewModel, "private async Task<bool> MutateAsync", "private Task RequestCommitAsync()");
        Assert.Contains("await mutation()", mutate);
        Assert.Contains("await RefreshStateAsync(includeHistory)", mutate);
    }

    private static Repository CreateDummyRepository() =>
        new("work", "work", ".git", false);

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker was not found: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker was not found: {endMarker}");
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
