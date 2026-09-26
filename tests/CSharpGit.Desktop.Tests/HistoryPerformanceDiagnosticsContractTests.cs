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
        Assert.Contains("[\"avatarCacheActivity\"]", source, StringComparison.Ordinal);
        Assert.Contains("AvatarDiskBytesWritten", source, StringComparison.Ordinal);
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
        Assert.Contains("HistoryGeometryUpdateReason.SizeChanged", graph, StringComparison.Ordinal);
        Assert.Contains("HistoryGeometryUpdateReason.PresentationContextChanged", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateGeometry(HistoryGeometryUpdateReason.DataContextChanged)", graph, StringComparison.Ordinal);
        Assert.Contains("GeometrySameKeySkipped", graph, StringComparison.Ordinal);
        Assert.Contains("GeometrySharedCacheHit", graph, StringComparison.Ordinal);
        Assert.Contains("GeometryBuilderCompleted", graph, StringComparison.Ordinal);
        Assert.Contains("TopologyConverted", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryItems.IndexOf", graph, StringComparison.Ordinal);
        Assert.DoesNotContain(".IndexOf(", graph, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphDiagnosticsSeparateTriggerCauseCacheAndSizeCategories()
    {
        var source = Read("src", "CSharpGit.Presentation", "Diagnostics", "HistoryPerformanceDiagnostics.cs");
        var graphDetail = Read("src", "CSharpGit.Presentation", "Diagnostics", "HistoryPerformanceSession.CommitGraph.cs");

        Assert.Contains("geometryBuildTriggers", source, StringComparison.Ordinal);
        Assert.Contains("geometryBuildCauses", source, StringComparison.Ordinal);
        Assert.Contains("geometrySameKeySkips", source, StringComparison.Ordinal);
        Assert.Contains("geometrySharedCacheHits", source, StringComparison.Ordinal);
        Assert.Contains("geometryMaterializationCalls", source, StringComparison.Ordinal);
        Assert.Contains("graphSizeChangedWidthOnly", source, StringComparison.Ordinal);
        Assert.Contains("HistoryGeometryBuildCause", graphDetail, StringComparison.Ordinal);
        Assert.Contains("GeometrySharedCacheEvictions", graphDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryScanDiagnosticsDistinguishPotentialLinearWork()
    {
        var source = Read("src", "CSharpGit.Presentation", "Diagnostics", "HistoryPerformanceDiagnostics.cs");
        var page = Read("src", "CSharpGit.Presentation", "MainPage.xaml.cs");

        Assert.Contains("HistoryGlobalScan", source, StringComparison.Ordinal);
        Assert.Contains("HistoryIndexLookup", source, StringComparison.Ordinal);
        Assert.Contains("CommitLookup", source, StringComparison.Ordinal);
        Assert.Contains("ParentLookup", source, StringComparison.Ordinal);
        Assert.Contains("RefLookup", source, StringComparison.Ordinal);
        Assert.Contains("ItemsExamined", source, StringComparison.Ordinal);
        Assert.Contains("CommitLookupCompleted", page, StringComparison.Ordinal);
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
        Assert.Contains("e.IsIntermediate", scroll, StringComparison.Ordinal);
        Assert.DoesNotContain("IndexOf", scroll, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", scroll, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureDoesNotReportInvalidRealizedEstimateAndFlagsOnlyActualAvatarNetwork()
    {
        var session = Read("src", "CSharpGit.Presentation", "Diagnostics", "HistoryPerformanceDiagnostics.cs");
        var facade = Read("src", "CSharpGit.Presentation", "Controls", "HistoryRenderDiagnostics.cs");

        Assert.DoesNotContain("EstimatedRealizedContainers", session, StringComparison.Ordinal);
        Assert.DoesNotContain("Max estimated realized", session, StringComparison.Ordinal);
        Assert.Contains("AuthorAvatarDiagnosticActivityKind.RemoteRequest", session, StringComparison.Ordinal);
        Assert.Contains("Volatile.Write(ref AvatarOnlineRequestDuringCapture, 1)", session, StringComparison.Ordinal);
        Assert.DoesNotContain("AvatarOnlineRequestDuringCapture", facade, StringComparison.Ordinal);
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

    [Fact]
    public void AvatarAndReferenceDiagnosticsExposeEffectiveWorkWithoutValues()
    {
        var session = Read("src", "CSharpGit.Presentation", "Diagnostics", "HistoryPerformanceDiagnostics.cs");
        var facade = Read("src", "CSharpGit.Presentation", "Controls", "HistoryRenderDiagnostics.cs");

        Assert.Contains("AvatarEffectiveStateTransitions", session, StringComparison.Ordinal);
        Assert.Contains("AvatarResolveDeduplicated", session, StringComparison.Ordinal);
        Assert.Contains("AvatarResolveCompletedNoImage", session, StringComparison.Ordinal);
        Assert.Contains("AvatarResolveFaulted", session, StringComparison.Ordinal);
        Assert.Contains("AvatarRequestsCompleted", session, StringComparison.Ordinal);
        Assert.Contains("ReferencesPresenterUpdates", session, StringComparison.Ordinal);
        Assert.Contains("ReferenceVisualsCreated", session, StringComparison.Ordinal);
        Assert.Contains("ReferenceVisualsReused", session, StringComparison.Ordinal);
        Assert.Contains("ReferencePresentationContextUpdates", session, StringComparison.Ordinal);
        Assert.Contains("SafeRatio", session, StringComparison.Ordinal);
        Assert.Contains("AvatarEffectiveStateTransition", facade, StringComparison.Ordinal);
        Assert.Contains("ReferenceVisualCreated", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"referenceName\"]", session, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"avatarIdentity\"]", session, StringComparison.Ordinal);
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
