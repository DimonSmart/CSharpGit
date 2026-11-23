namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeCommitEditorOwnershipContractTests
{
    [Fact]
    public void CommitMessageAndButtonsBelongToWorkingTreeEditor()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var page = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeView.xaml"));
        var editor = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeCommitEditorView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeCommitEditorView.xaml.cs"));
        var owner = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryMaintenance.cs"));
        var lifetime = File.ReadAllText(Path.Combine(presentation, "MainPage.Lifecycle.cs"));

        Assert.Contains("<views:WorkingTreeCommitEditorView", page);
        Assert.DoesNotContain("x:Name=\"CommitMessageEditor\"", page);
        Assert.Contains("x:Name=\"CommitMessageEditor\"", editor);
        Assert.Contains("CommitCreation.CommitCommand", editor);
        Assert.Contains("CommitCreation.AmendCommand", editor);
        Assert.Contains("Stashes.CanCreateStash", editor);
        Assert.Contains("Click=\"Stash_Click\"", editor);
        Assert.Contains("StashRequested?.Invoke", codeBehind);
        Assert.Contains("WorkingTreeCommitEditor.StashRequested += CreateStash_Click;", owner);
        Assert.Contains("WorkingTreeCommitEditor.StashRequested -= CreateStash_Click;", lifetime);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
