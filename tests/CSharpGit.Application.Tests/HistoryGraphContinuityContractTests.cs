namespace CSharpGit.Application.Tests;

public sealed class HistoryGraphContinuityContractTests
{
    [Fact]
    public void HistoryLayoutKeepsGraphSurfaceContinuousWithoutOwningFixedHeight()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var graphControl = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphControl.cs"));
        var graphMetrics = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphMetrics.cs"));

        var historyStyle = Slice(workspace,
            "<Style x:Key=\"HistoryRowStyle\"",
            "</Style>");
        var historyTemplate = Slice(workspace,
            "<DataTemplate x:Key=\"HistoryItemTemplate\"",
            "</DataTemplate>");

        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", historyStyle);
        Assert.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Stretch\" />", historyStyle);
        Assert.Contains("MinHeight=\"{StaticResource Height.DataRow}\"", historyTemplate);
        Assert.Contains("VerticalAlignment=\"Stretch\"", historyTemplate);
        Assert.DoesNotContain("Height=\"34\"", historyStyle);
        Assert.DoesNotContain("Height=\"34\"", historyTemplate);
        Assert.DoesNotContain("DefaultRowHeight", graphControl);
        Assert.DoesNotContain("DefaultRowHeight", graphMetrics);
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {startMarker}");
        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= start, $"Missing marker: {endMarker}");
        return value[start..(end + endMarker.Length)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
