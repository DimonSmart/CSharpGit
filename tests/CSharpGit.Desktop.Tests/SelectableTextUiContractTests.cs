namespace CSharpGit.Desktop.Tests;

public sealed class SelectableTextUiContractTests
{
    [Fact]
    public void InformationalValuesUseSemanticSelectablePresentation()
    {
        var root = FindRepositoryRoot();
        var typography = Read(root, "src", "CSharpGit.Presentation", "Styles", "Typography.xaml");
        var details = Read(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml");
        var commitTime = Read(root, "src", "CSharpGit.Presentation", "Controls", "CommitTimeText.cs");
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var console = Read(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml");
        var preview = Read(root, "src", "CSharpGit.Presentation", "Controls", "FilePreviewHost.cs");

        Assert.Contains("SelectableBodyTextStyle", typography, StringComparison.Ordinal);
        Assert.Contains("SelectableTechnicalTextStyle", typography, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabled", typography, StringComparison.Ordinal);

        Assert.Contains("Style=\"{StaticResource SelectableBodyTextStyle}\"", details, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SelectableTechnicalTextStyle}\"", details, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabled=\"True\"", details, StringComparison.Ordinal);
        Assert.Contains("Copy commit message", details, StringComparison.Ordinal);
        Assert.Contains("Copy commit hash", details, StringComparison.Ordinal);
        Assert.Contains("Copy parent hash", details, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabledProperty", commitTime, StringComparison.Ordinal);

        Assert.Contains("Repository.WorkingDirectory", main, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SelectableTechnicalTextStyle}\"", main, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"CommandText\"", console, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WorkingDirectoryText\"", console, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartedText\"", console, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DurationText\"", console, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExitCodeText\"", console, StringComparison.Ordinal);
        Assert.Contains("SelectableTechnicalTextStyle", console, StringComparison.Ordinal);
        Assert.Contains("SelectableBodyTextStyle", console, StringComparison.Ordinal);
        Assert.Contains("Copy command", console, StringComparison.Ordinal);
        Assert.Contains("Copy all", console, StringComparison.Ordinal);

        Assert.Contains("IsTextSelectionEnabled = true", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitAndWorkingTreeDiffsShareOneSelectableViewer()
    {
        var root = FindRepositoryRoot();
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var viewerXaml = Read(root, "src", "CSharpGit.Presentation", "Controls", "SelectableDiffViewer.xaml");
        var viewerCode = Read(root, "src", "CSharpGit.Presentation", "Controls", "SelectableDiffViewer.xaml.cs");
        var changes = Read(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs");
        var workingTree = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeDiff.cs");

        Assert.Contains("controls:SelectableDiffViewer x:Name=\"CompactDiffViewer\"", main, StringComparison.Ordinal);
        Assert.Contains("controls:SelectableDiffViewer x:Name=\"WorkingTreeDiffViewer\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"CompactDiffList\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"WorkingTreeCompactDiffList\"", main, StringComparison.Ordinal);

        Assert.Contains("ItemsRepeater x:Name=\"RowsRepeater\"", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"38,38,*\"", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DiffText\"", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DiffDiagnosticsInfo\"", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("SelectableDiffTextStyle", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", viewerXaml, StringComparison.Ordinal);

        Assert.Contains("new DiffLogicalText(snapshot)", viewerCode, StringComparison.Ordinal);
        Assert.Contains("DiffText.Text = string.Empty", viewerCode, StringComparison.Ordinal);
        Assert.Contains("RowsRepeater.ItemsSource = null", viewerCode, StringComparison.Ordinal);
        Assert.Contains("DiffText.SelectAll()", viewerCode, StringComparison.Ordinal);
        Assert.Contains("DiffText.CopySelectionToClipboard()", viewerCode, StringComparison.Ordinal);
        Assert.Contains("DiffText.SelectedText", viewerCode, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsMacOS()", viewerCode, StringComparison.Ordinal);
        Assert.Contains("_viewportResetGate.BeginReplacement()", viewerCode, StringComparison.Ordinal);
        Assert.Contains("DiffScroller.ChangeView(0d, 0d, null, true)", viewerCode, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue(() => ResetViewport(ticket))", viewerCode, StringComparison.Ordinal);
        Assert.Contains("_viewportResetGate.RegisterInteraction()", viewerCode, StringComparison.Ordinal);

        Assert.Contains("CompactDiffViewer.SetLines(compactLines, diff.Diagnostics)", changes, StringComparison.Ordinal);
        Assert.Contains("CompactDiffViewer.Clear()", changes, StringComparison.Ordinal);
        Assert.Contains("WorkingTreeDiffViewer.SetLines(compactLines, diff.Diagnostics)", workingTree, StringComparison.Ordinal);
        Assert.Contains("WorkingTreeDiffViewer.Clear()", workingTree, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] path)
    {
        var parts = new string[path.Length + 1];
        parts[0] = root;
        Array.Copy(path, 0, parts, 1, path.Length);
        return File.ReadAllText(Path.Combine(parts));
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
