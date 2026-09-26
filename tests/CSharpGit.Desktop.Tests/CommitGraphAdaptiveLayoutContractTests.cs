namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphAdaptiveLayoutContractTests
{
    [Fact]
    public void HeaderAndRowsDoNotOwnIndependentFixedGraphWidths()
    {
        var root = FindRepositoryRoot();
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var historyReferences = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));

        Assert.DoesNotContain("120,*,160,150,90", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("120,*,160,150,90", historyReferences, StringComparison.Ordinal);
        Assert.Contains("HistoryGraphHeaderColumn", mainPage, StringComparison.Ordinal);
        Assert.Contains("HistoryRowRoot", historyReferences, StringComparison.Ordinal);
        Assert.Contains(@"<ColumnDefinition Width=""Auto"" />", historyReferences, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainerContentChanging=", mainPage, StringComparison.Ordinal);
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitGraphLayout.cs"));
        Assert.Contains("CommitGraphPresentationContext.Publish", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", lifecycle, StringComparison.Ordinal);
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
    public void GraphControlUsesStructuralGeometryIdentityAndOwnsHardClip()
    {
        var control = ReadControl();

        Assert.Contains("CommitGraphGeometryKey", control, StringComparison.Ordinal);
        Assert.Contains("GeometrySameKeySkipped", control, StringComparison.Ordinal);
        Assert.Contains("GeometrySharedCacheHit", control, StringComparison.Ordinal);
        Assert.Contains("Clip = _clipGeometry", control, StringComparison.Ordinal);
        Assert.Contains("_pathGeometries", control, StringComparison.Ordinal);
        Assert.Contains("_nodeGeometry", control, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceEquals(_renderedGraph", control, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationOnlyChangesDoNotInvalidateGeometry()
    {
        var control = ReadControl();
        var theme = MethodBlock(control, "private void HandleThemeChanged()");
        var dataContext = MethodBlock(control, "private void HandleDataContextChanged()");
        var metrics = MethodBlock(control, "private void ApplyMetrics(");

        Assert.DoesNotContain("UpdateGeometry", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateGeometry", dataContext, StringComparison.Ordinal);
        Assert.Contains("lineThicknessChanged", metrics, StringComparison.Ordinal);
        Assert.Contains("requestGeometryUpdate && geometryChanged", metrics, StringComparison.Ordinal);
        Assert.DoesNotContain("_renderedTheme", control, StringComparison.Ordinal);
    }

    [Fact]
    public void SizeChangedSeparatesWidthFirstHeightAndInsignificantChanges()
    {
        var control = ReadControl();
        var sizeChanged = MethodBlock(control, "private void HandleSizeChanged(SizeChangedEventArgs args)");

        Assert.Contains("GraphSizeChangedWidthOnly", sizeChanged, StringComparison.Ordinal);
        Assert.Contains("GraphSizeChangedHeightChanged", sizeChanged, StringComparison.Ordinal);
        Assert.Contains("GraphSizeChangedInsignificant", sizeChanged, StringComparison.Ordinal);
        Assert.Contains("GraphSizeChangedFirstValidHeight", sizeChanged, StringComparison.Ordinal);
        Assert.Contains("CommitGraphGeometryKey.GeometryEpsilon", sizeChanged, StringComparison.Ordinal);
    }

    [Fact]
    public void MeasureAndArrangeRemainGeometryFree()
    {
        var control = ReadControl();
        var measure = MethodBlock(control, "protected override Size MeasureOverride(Size availableSize)");
        var arrange = MethodBlock(control, "protected override Size ArrangeOverride(Size finalSize)");

        Assert.DoesNotContain("CommitGraphGeometryBuilder.Build", measure, StringComparison.Ordinal);
        Assert.DoesNotContain("MaterializeGeometry", measure, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateGeometry", measure, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitGraphGeometryBuilder.Build", arrange, StringComparison.Ordinal);
        Assert.DoesNotContain("MaterializeGeometry", arrange, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateGeometry", arrange, StringComparison.Ordinal);
    }

    private static string ReadControl()
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Controls",
            "CommitGraph",
            "CommitGraphControl.cs"));
    }

    private static string MethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"Method body not found: {signature}");

        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return source[start..(index + 1)];
                    break;
            }
        }

        throw new InvalidOperationException($"Method body is incomplete: {signature}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
