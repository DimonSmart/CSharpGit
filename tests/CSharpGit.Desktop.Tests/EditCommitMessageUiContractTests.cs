namespace CSharpGit.Desktop.Tests;

public sealed class EditCommitMessageUiContractTests
{
    [Fact]
    public void CommitMenuExposesEditActionWithCheapAvailabilityChecks()
    {
        var actions = Read("src", "CSharpGit.Presentation", "Workflows", "CommitActionController.cs");

        Assert.Contains("Edit commit message…", actions, StringComparison.Ordinal);
        Assert.Contains("_editCommitMessageItem.Click += EditCommitMessage_Click", actions, StringComparison.Ordinal);
        Assert.Contains("_editCommitMessage.EditAsync", actions, StringComparison.Ordinal);
        Assert.Contains("_editCommitMessageItem.IsEnabled = canMutate;", actions, StringComparison.Ordinal);
        Assert.Contains("_historyView.List.SelectedItem = row;", actions, StringComparison.Ordinal);
        Assert.Contains("_viewModel.History.SelectedRow = row;", actions, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogUsesFullMultilineMessageAndGuardsPrimaryAction()
    {
        var workflow = Read("src", "CSharpGit.Presentation", "Workflows", "EditCommitMessageController.cs");

        Assert.Contains("Text = originalMessage", workflow, StringComparison.Ordinal);
        Assert.Contains("var originalMessage = commit.Message;", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("commit.Subject", workflow, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = true", workflow, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", workflow, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 520", workflow, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 180", workflow, StringComparison.Ordinal);
        Assert.Contains("Title = \"Edit commit message\"", workflow, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Change message\"", workflow, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(candidate)", workflow, StringComparison.Ordinal);
        Assert.Contains("string.Equals(originalMessage, candidate, StringComparison.Ordinal)", workflow, StringComparison.Ordinal);
        Assert.Contains("!_viewModel.IsBusy", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RepositoryOperations.CurrentOperation == RepositoryOperation.None", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void UiDelegatesGitSemanticsAndRestoresServiceComputedSelection()
    {
        var workflow = Read("src", "CSharpGit.Presentation", "Workflows", "EditCommitMessageController.cs");
        var actions = Read("src", "CSharpGit.Presentation", "Workflows", "CommitActionController.cs");
        var page = Read("src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var rewriteOwner = Read("src", "CSharpGit.Presentation.Core", "ViewModels", "CommitActionsViewModel.cs");

        Assert.Contains("_viewModel.CommitActions.EditCommitMessageAsync(", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_commitActionService", workflow, StringComparison.Ordinal);
        Assert.Contains("_commitActionService.EditCommitMessageAsync(", rewriteOwner, StringComparison.Ordinal);
        Assert.Contains("result.ConflictNewCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("result.ConflictOldCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("_pendingEditedCommitHash = result.ConflictNewCommit;", workflow, StringComparison.Ordinal);
        Assert.Contains("new RepositoryIdleContinuation(_viewModel", workflow, StringComparison.Ordinal);
        Assert.Contains("runUiOperation(RestorePendingEditedCommitSelectionAsync", workflow, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "MainPage.EditCommitMessage.cs")));
        Assert.Contains("internal async Task<bool> RestoreSelectionAsync", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("git rebase", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git commit", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAsync(", workflow, StringComparison.Ordinal);
    }

    private static string Read(params string[] path)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root, .. path]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
