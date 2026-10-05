namespace CSharpGit.Application.Tests;

public sealed class ReflogHistoryUiContractTests
{
    [Fact]
    public void ReflogUsesTypedReferenceDecorationsAndBoundedGhostMetadata()
    {
        var root = FindRepositoryRoot();
        var historyReferences = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var presenter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencesPresenter.cs"));
        var domain = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "History.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var git = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitReferenceHistoryService.cs"));

        Assert.Contains("HistoryReferenceDecoration", domain);
        Assert.Contains("HistoryReferenceKind", domain);
        Assert.Contains("ReflogPresentation", domain);
        Assert.Contains("ReferenceDetails", domain);

        Assert.Contains("References=\"{x:Bind ReferenceDetails, Mode=OneWay}\"", historyReferences);
        Assert.Contains("Text=\"reflog\"", historyReferences);
        Assert.Contains("ReflogGhostDisplay", historyReferences);
        Assert.Contains("ReflogToolTip", historyReferences);
        Assert.Contains("HistoryReflogGhostTextStyle", historyReferences);
        Assert.Contains("IReadOnlyList<HistoryReferenceDecoration>", presenter);
        Assert.DoesNotContain("startsWith(\"origin/\"", presenter, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("ReflogSessionId", viewModel);
        Assert.Contains("RepositoryReferences: new GitReferences(", viewModel);
        Assert.Contains("BuildReferenceDetails(query)", git);
        Assert.Contains("ReflogMetadataRecordLimit", git);
        Assert.Contains("\"reflog\"", git);
        Assert.Contains("\"show\"", git);
        Assert.Contains("\"--all\"", git);
        Assert.Contains("%gD%x00%gd%x00%gs%x1e", git);
        Assert.Contains("MaxCachedReflogSessions", git);
        Assert.DoesNotContain(".git/logs", git);
        Assert.DoesNotContain("git log -g", git, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReflogGraphStylingIsRowLocalPresentationOnly()
    {
        var root = FindRepositoryRoot();
        var historyReferences = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var graphControl = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphControl.cs"));
        var trackPresentation = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphTrackPresentation.cs"));
        var graphVisual = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphRowVisual.cs"));
        var geometryKey = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphGeometryKey.cs"));
        var converter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitTopologyToGraphVisualConverter.cs"));
        var domain = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "History.cs"));

        var graphTemplateCount = CountOccurrences(historyReferences, "<controls:CommitGraphControl ");
        var reflogBindingCount = CountOccurrences(
            historyReferences,
            "IsReflogOnly=\"{x:Bind IsReflogOnly, Mode=OneWay}\"");

        Assert.Equal(3, graphTemplateCount);
        Assert.Equal(graphTemplateCount, reflogBindingCount);
        Assert.Contains("public static readonly DependencyProperty IsReflogOnlyProperty", graphControl);
        Assert.Contains("LightReflogColor", graphControl);
        Assert.Contains("DarkReflogColor", graphControl);
        Assert.Contains("CommitGraphTrackPresentation.ShouldUseMutedStyle", graphControl);
        Assert.Contains("isReflogOnly && trackId == nodeTrackId", trackPresentation);

        var callback = Slice(
            graphControl,
            "private static void OnIsReflogOnlyChanged(",
            "private void UpdateGeometry(");
        Assert.Contains("control.Invalidate();", callback);
        Assert.DoesNotContain("UpdateGeometry(", callback);

        Assert.DoesNotContain("IsReflogOnly", graphVisual);
        Assert.DoesNotContain("IsReflogOnly", geometryKey);
        Assert.DoesNotContain("IsReflogOnly", converter);

        var topology = Slice(
            domain,
            "public sealed record CommitTopology",
            "public sealed record HistoryRow");
        Assert.DoesNotContain("IsReflogOnly", topology);
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {startMarker}");
        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= start, $"Missing marker: {endMarker}");
        return value[start..end];
    }

    private static int CountOccurrences(string value, string marker)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
