namespace CSharpGit.Application.Tests;

public sealed class HistoryMessageWrappingUiContractTests
{
    [Fact]
    public void HistorySubjectRemainsSingleLineAndCommitDetailsWrapsFullMessage()
    {
        var root = FindRepositoryRoot();
        var historyXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Styles",
            "HistoryReferences.xaml"));
        var detailsXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Controls",
            "CommitDetailsView.xaml"));
        var detailsHost = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "MainPage.CommitCopy.cs"));
        var appHost = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "App.xaml.cs"));

        var subject = ExtractElement(historyXaml, "<TextBlock Text=\"{Binding Commit.Subject}\"");
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", subject);
        Assert.DoesNotContain("TextWrapping=", subject);

        Assert.Contains("<Grid ColumnDefinitions=\"*,Auto\"", detailsXaml);
        var message = ExtractElement(detailsXaml, "<TextBlock Text=\"{Binding SelectedHistoryRow.Commit.Message}\"");
        Assert.Contains("TextWrapping=\"Wrap\"", message);

        Assert.Contains("DetailsScroller.HorizontalScrollMode = ScrollMode.Disabled", detailsHost);
        Assert.Contains("DetailsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", detailsHost);
        Assert.Contains("DetailsScroller.SizeChanged += DetailsScroller_SizeChanged", detailsHost);
        Assert.Contains("DetailsScroller.DispatcherQueue.TryEnqueue(ConstrainCommitDetailsToViewport)", detailsHost);
        Assert.DoesNotContain("ActualWidth", detailsHost);
        Assert.Contains("HistoryPane.TransformToVisual(null).TransformPoint(default)", detailsHost);
        Assert.Contains("app.MainWindowClientWidth / rasterizationScale", detailsHost);
        Assert.Contains("Math.Min(DetailsScroller.ViewportWidth, windowWidth - origin.X)", detailsHost);
        Assert.Contains("!double.IsFinite(_commitDetailsView.Width)", detailsHost);
        Assert.Contains("internal double MainWindowClientWidth => _window?.AppWindow.ClientSize.Width ?? 0", appHost);
    }

    private static string ExtractElement(string xaml, string marker)
    {
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return xaml[start..(end + 2)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
