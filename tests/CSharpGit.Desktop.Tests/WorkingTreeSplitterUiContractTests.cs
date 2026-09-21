namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeSplitterUiContractTests
{
    [Fact]
    public void WorkingTreeStagedAndUnstagedAreasHaveResizableSplitter()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));

        const string splitterName = "x:Name=\"WorkingTreeStagingSplitter\"";
        var splitterStart = xaml.IndexOf(splitterName, StringComparison.Ordinal);
        Assert.True(splitterStart >= 0);

        var splitterEnd = xaml.IndexOf("/>", splitterStart, StringComparison.Ordinal);
        Assert.True(splitterEnd > splitterStart);

        var splitter = xaml[splitterStart..(splitterEnd + 2)];
        Assert.Contains("Grid.Row=\"1\"", splitter, StringComparison.Ordinal);
        Assert.Contains("ResizeDirection=\"Rows\"", splitter, StringComparison.Ordinal);
        Assert.Contains("ResizeBehavior=\"PreviousAndNext\"", splitter, StringComparison.Ordinal);
        Assert.Contains("MinimumFirst=\"80\"", splitter, StringComparison.Ordinal);
        Assert.Contains("MinimumSecond=\"80\"", splitter, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
