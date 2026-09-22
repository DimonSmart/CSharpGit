namespace CSharpGit.Desktop.Tests;

public sealed class EditCommitMessageUiContractTests
{
    [Fact]
    public void CommitMenuExposesEditActionWithCheapAvailabilityChecks()
    {
        var actions = Read("src", "CSharpGit.Presentation", "MainPage.CommitActions.cs");

        Assert.Contains("Edit commit message…", actions, StringComparison.Ordinal);
        Assert.Contains("_editCommitMessageItem.Click += EditCommitMessage_Click;", actions, StringComparison.Ordinal);
        Assert.Contains("_editCommitMessageItem.IsEnabled = canMutate;", actions, StringComparison.Ordinal);
        Assert.Contains("HistoryList.SelectedItem = row;", actions, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectedHistoryRow = row;", actions, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogUsesFullMultilineMessageAndGuardsPrimaryAction()
    {
        var workflow = Read("src", "CSharpGit.Presentation", "MainPage.EditCommitMessage.cs");

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
        Assert.Contains("_viewModel.CurrentOperation == RepositoryOperation.None", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void UiDelegatesGitSemanticsAndRestoresServiceComputedSelection()
    {
        var workflow = Read("src", "CSharpGit.Presentation", "MainPage.EditCommitMessage.cs");
        var actions = Read("src", "CSharpGit.Presentation", "MainPage.CommitActions.cs");
        var page = Read("src", "CSharpGit.Presentation", "MainPage.xaml.cs");

        Assert.Contains("_commitActionService.EditCommitMessageAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("result.NewCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("result.OldCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("_pendingEditedCommitHash = result.NewCommit;", workflow, StringComparison.Ordinal);
        Assert.Contains("RestorePendingEditedCommitSelectionAsync", page, StringComparison.Ordinal);
        Assert.Contains("private async Task<bool> RestoreCommitActionSelectionAsync", actions, StringComparison.Ordinal);
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
