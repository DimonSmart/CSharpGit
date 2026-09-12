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
    public void WorkingTreeUiExposesSelectedAndAllDiscardWithDestructiveConfirmation()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Discard.cs"));

        Assert.Contains("Command=\"{Binding RequestDiscardSelectedCommand}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Discard selected\"", xaml);
        Assert.Contains("Command=\"{Binding RequestDiscardAllCommand}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Discard all…\"", xaml);
        Assert.Contains("Foreground=\"{ThemeResource SystemFillColorCriticalBrush}\"", xaml);
        Assert.Contains("Message=\"{Binding BatchDiscardConfirmationMessage}\"", xaml);
        Assert.Contains("Command=\"{Binding CancelBatchDiscardCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding ConfirmBatchDiscardCommand}\"", xaml);
        Assert.Contains("Background=\"{ThemeResource SystemFillColorCriticalBrush}\"", xaml);

        Assert.Contains("WorkingTreeDiscard.CreateSelected(_selectedUnstagedChanges)", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.CreateAll(Changes)", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.ExecuteAsync", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.FormatFailures(results)", discardViewModel);
    }

    [Fact]
    public void CancelConfirmationDoesNotRunDiscardMutation()
    {
        var root = FindRepositoryRoot();
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Discard.cs"));
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
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Discard.cs"));

        var confirm = ExtractMethod(discardViewModel, "private async Task ConfirmBatchDiscardAsync()", "private void SetPendingBatchDiscard");
        Assert.Contains("await MutateAsync", confirm);
        Assert.Contains("WorkingTreeDiscard.ExecuteAsync", confirm);
        Assert.Contains("WorkingTreeDiscard.FormatFailures(results)", confirm);
        Assert.Contains("includeHistory: false", confirm);

        var mutate = ExtractMethod(viewModel, "private async Task<bool> MutateAsync", "private async Task RunConflictActionAsync");
        Assert.Contains("await mutation()", mutate);
        Assert.Contains("await RefreshStateAsync(includeHistory)", mutate);
    }

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
