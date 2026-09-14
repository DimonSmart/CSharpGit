namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphAdaptiveLayoutContractTests
{
    [Fact]
    public void HeaderAndRowsDoNotOwnIndependentFixedGraphWidths()
    {
        var root = FindRepositoryRoot();
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));

        Assert.DoesNotContain("120,*,160,150,90", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("120,*,160,150,90", workspace, StringComparison.Ordinal);
        Assert.Contains("HistoryGraphHeaderColumn", mainPage, StringComparison.Ordinal);
        Assert.Contains("HistoryRowRoot", workspace, StringComparison.Ordinal);
        Assert.Contains("HistoryList_ContainerContentChanging", mainPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ConverterConsumesPersistedLaneCountWithoutScanningEdges()
    {
        var root = FindRepositoryRoot();
        var converter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitTopologyToGraphVisualConverter.cs"));

        Assert.Contains("var laneCount = topology.LaneCount;", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectMany", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("allEdges", converter, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphControlCachesEffectiveMetricsAndOwnsHardClip()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphControl.cs"));

        Assert.Contains("_renderedMetrics != Metrics", control, StringComparison.Ordinal);
        Assert.Contains("Clip = _clipGeometry", control, StringComparison.Ordinal);
        Assert.Contains("CommitGraphGeometryBuilder.Build(Graph, height, Metrics)", control, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
