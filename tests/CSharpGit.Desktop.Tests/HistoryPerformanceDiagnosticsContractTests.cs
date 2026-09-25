namespace CSharpGit.Desktop.Tests;

public sealed class HistoryPerformanceDiagnosticsContractTests
{
    [Fact]
    public void CaptureUsesBoundedBuffersAndBackgroundJsonlWriter()
    {
        var source = Read("src", "CSharpGit.Presentation", "Diagnostics", "HistoryPerformanceDiagnostics.cs");

        Assert.Contains("SlowOperationCapacity = 2048", source, StringComparison.Ordinal);
        Assert.Contains("Channel.CreateBounded<string>", source, StringComparison.Ordinal);
        Assert.Contains("BoundedChannelFullMode.Wait", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run(WriterLoopAsync)", source, StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer(TimeSpan.FromSeconds(1))", source, StringComparison.Ordinal);
        Assert.Contains("[\"type\"] = \"sessionSummary\"", source, StringComparison.Ordinal);
        Assert.Contains(".summary.txt", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionVirtualizationSubscriptionExistsOnlyInsideActiveCaptureLifecycle()
    {
        var page = Read("src", "CSharpGit.Presentation", "MainPage.HistoryPerformanceDiagnostics.cs");
        var infiniteScroll = Read("src", "CSharpGit.Presentation", "MainPage.HistoryInfiniteScroll.cs");

        Assert.Contains("HistoryList.ContainerContentChanging += HistoryList_PerformanceContainerContentChanging", page, StringComparison.Ordinal);
        Assert.Contains("HistoryList.ContainerContentChanging -= HistoryList_PerformanceContainerContentChanging", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainerContentChanging", infiniteScroll, StringComparison.Ordinal);
        Assert.DoesNotContain("FindDescendant", page, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", page, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureOutputIsPrivacySafeByContract()
    {
        var source = Read("src", "CSharpGit.Presentation", "Diagnostics", "HistoryPerformanceDiagnostics.cs");

        Assert.DoesNotContain("[\"repositoryPath\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"repositoryName\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"commitMessage\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"commitSha\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"authorName\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"authorEmail\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"remoteUrl\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"gitArguments\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"gitStdout\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"gitStderr\"]", source, StringComparison.Ordinal);
        Assert.Contains("[\"gitCommandCategories\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HotPathFacadeKeepsLegacyCheckAndUsesCheapActiveSessionGuard()
    {
        var source = Read("src", "CSharpGit.Presentation", "Controls", "HistoryRenderDiagnostics.cs");

        Assert.Contains("CSHARPGIT_HISTORY_RENDER_DIAGNOSTICS", source, StringComparison.Ordinal);
        Assert.Contains("EnableForCheck()", source, StringComparison.Ordinal);
        Assert.Contains("HistoryPerformanceDiagnostics.ActiveSession", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphInstrumentationReportsReasonsAndDoesNotSearchHistoryForRowIndex()
    {
        var graph = Read("src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphControl.cs");
        var converter = Read("src", "CSharpGit.Presentation", "Controls", "CommitTopologyToGraphVisualConverter.cs");

        Assert.Contains("HistoryGeometryUpdateReason.GraphChanged", graph, StringComparison.Ordinal);
        Assert.Contains("HistoryGeometryUpdateReason.DataContextChanged", graph, StringComparison.Ordinal);
        Assert.Contains("HistoryGeometryUpdateReason.SizeChanged", graph, StringComparison.Ordinal);
        Assert.Contains("HistoryGeometryUpdateReason.PresentationContextChanged", graph, StringComparison.Ordinal);
        Assert.Contains("GeometryCacheHit", graph, StringComparison.Ordinal);
        Assert.Contains("GeometryBuilderCompleted", graph, StringComparison.Ordinal);
        Assert.Contains("TopologyConverted", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryItems.IndexOf", graph, StringComparison.Ordinal);
        Assert.DoesNotContain(".IndexOf(", graph, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutAndScrollInstrumentationDoesNotAddFullHistoryScanToScrolling()
    {
        var layout = Read("src", "CSharpGit.Presentation", "MainPage.CommitGraphLayout.cs");
        var scroll = Read("src", "CSharpGit.Presentation", "MainPage.HistoryInfiniteScroll.cs");

        Assert.Contains("GraphLayoutInitialized", layout, StringComparison.Ordinal);
        Assert.Contains("GraphLayoutUpdated", layout, StringComparison.Ordinal);
        Assert.Contains("PresentationPublished", Read("src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphPresentationContext.cs"), StringComparison.Ordinal);
        Assert.Contains("HistoryViewChanged", scroll, StringComparison.Ordinal);
        Assert.Contains("PageLoadStarted", scroll, StringComparison.Ordinal);
        Assert.DoesNotContain("IndexOf", scroll, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", scroll, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureHasAllRequiredTerminationPaths()
    {
        var page = Read("src", "CSharpGit.Presentation", "MainPage.HistoryPerformanceDiagnostics.cs");
        var lifecycle = Read("src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs");

        Assert.Contains("HistoryPerformanceStopReason.Manual", page, StringComparison.Ordinal);
        Assert.Contains("HistoryPerformanceStopReason.RepositoryClosed", page, StringComparison.Ordinal);
        Assert.Contains("HistoryPerformanceStopReason.RepositoryChanged", page, StringComparison.Ordinal);
        Assert.Contains("HistoryPerformanceStopReason.DiagnosticsDisabled", page, StringComparison.Ordinal);
        Assert.Contains("HistoryPerformanceStopReason.Timeout", page, StringComparison.Ordinal);
        Assert.Contains("HistoryPerformanceMaximumDuration = TimeSpan.FromMinutes(10)", page, StringComparison.Ordinal);
        Assert.Contains("StopHistoryPerformanceCaptureOnShutdown", lifecycle, StringComparison.Ordinal);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(path).ToArray()));

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
