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
        var mainPageXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "MainPage.xaml"));
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
        var lifecycle = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "MainPage.Lifecycle.cs"));
        var confirmationDialogs = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "MainPage.ConfirmationDialogs.cs"));
        var appHost = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "App.xaml.cs"));

        var subject = ExtractElement(historyXaml, "<TextBlock Text=\"{x:Bind Commit.Subject, Mode=OneWay}\"");
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", subject);
        Assert.DoesNotContain("TextWrapping=", subject);

        Assert.Contains("<Grid ColumnDefinitions=\"*,Auto\"", detailsXaml);
        var message = ExtractElement(detailsXaml, "<TextBlock Text=\"{Binding SelectedHistoryRow.Commit.Message}\"");
        Assert.Contains("TextWrapping=\"Wrap\"", message);
        Assert.Contains("Grid.Column=\"1\"", detailsXaml);
        Assert.Contains("ToolTipService.ToolTip=\"Copy commit message\"", detailsXaml);

        var detailsScroller = ExtractOpeningTag(mainPageXaml, "<ScrollViewer x:Name=\"DetailsScroller\"");
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", detailsScroller);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", detailsScroller);
        Assert.Contains("VerticalScrollMode=\"Auto\"", detailsScroller);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", detailsScroller);
        Assert.Contains("HorizontalAlignment=\"Left\"", detailsScroller);
        Assert.Contains("<controls:CommitDetailsView x:Name=\"CommitDetailsContent\" />", mainPageXaml);
        Assert.Equal(1, CountOccurrences(mainPageXaml, "<controls:CommitDetailsView"));
        Assert.DoesNotContain("Text=\"{Binding SelectedHistoryRow.Commit.Message}\"", mainPageXaml);
        Assert.Contains("Loaded=\"MainPage_Loaded\"", mainPageXaml);

        Assert.DoesNotContain("new CommitDetailsView", detailsHost);
        Assert.DoesNotContain("DetailsScroller.Content =", detailsHost);
        Assert.Contains("_commitDetailsView = CommitDetailsContent", detailsHost);
        Assert.Contains("DetailsScroller.HorizontalScrollMode = ScrollMode.Disabled", detailsHost);
        Assert.Contains("DetailsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", detailsHost);
        Assert.Contains("DetailsScroller.SizeChanged += DetailsScroller_SizeChanged", detailsHost);
        Assert.Contains("DetailsScroller.DispatcherQueue.TryEnqueue(ConstrainCommitDetailsToViewport)", detailsHost);
        Assert.DoesNotContain("ActualWidth", detailsHost);
        Assert.Contains("HistoryPane.TransformToVisual(null).TransformPoint(default)", detailsHost);
        Assert.Contains("XamlRoot.RasterizationScale", detailsHost);
        Assert.Contains("app.MainWindowClientWidth / rasterizationScale", detailsHost);
        Assert.Contains("var visibleWidth = windowWidth - origin.X", detailsHost);
        Assert.Contains("DetailsScroller.Width = visibleWidth", detailsHost);
        Assert.Contains("if (eventArgs.DidSizeChange) mainPage.QueueCommitDetailsLayout()", appHost);
        Assert.Contains("!double.IsFinite(_commitDetailsView.Width)", detailsHost);
        Assert.Contains("InitializeCommitDetailsSurface();", lifecycle);
        Assert.DoesNotContain("InitializeCommitDetailsSurface", confirmationDialogs);
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

    private static string ExtractOpeningTag(string xaml, string marker)
    {
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = xaml.IndexOf('>', start);
        Assert.True(end > start);
        return xaml[start..(end + 1)];
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
