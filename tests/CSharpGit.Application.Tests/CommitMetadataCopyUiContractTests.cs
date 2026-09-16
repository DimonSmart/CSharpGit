namespace CSharpGit.Application.Tests;

public sealed class CommitMetadataCopyUiContractTests
{
    [Fact]
    public void CommitDetailsExposeExactMetadataCopyActions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml.cs"));
        var mainPageXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var wiring = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitCopy.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs"));
        var history = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "History.cs"));

        Assert.Contains("Tag=\"{Binding SelectedHistoryRow.Commit.Message}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Copy commit message\"", xaml);
        Assert.Contains("Tag=\"{Binding SelectedHistoryRow.Commit.Hash}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Copy commit hash\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding SelectedHistoryRow.Commit.Parents}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Copy parent hash\"", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.IsRootCommit", xaml);
        Assert.Contains("Clipboard.SetContent(package)", codeBehind);
        Assert.Contains("icon.Glyph = \"\\uE73E\"", codeBehind);
        Assert.Contains("<controls:CommitDetailsView x:Name=\"CommitDetailsContent\" />", mainPageXaml);
        Assert.DoesNotContain("new CommitDetailsView", wiring);
        Assert.DoesNotContain("DetailsScroller.Content =", wiring);
        Assert.Contains("InitializeCommitDetailsSurface();", lifecycle);
        Assert.Contains("public bool IsRootCommit => Parents.Count == 0;", history);
        Assert.Contains("IsRootCommit ? \"—\"", history);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
