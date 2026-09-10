namespace CSharpGit.Application.Tests;

public sealed class UnoXamlCompatibilityContractTests
{
    [Fact]
    public void DenseListItemStylePreservesUnoSelectionVisualStates()
    {
        var workspace = ReadWorkspace();
        var denseStyle = Slice(workspace,
            "<Style x:Key=\"DenseListItemStyle\"",
            "</Style>");

        Assert.Contains("BasedOn=\"{StaticResource ListViewItemExpanded}\"", denseStyle);
        Assert.DoesNotContain("<Setter Property=\"Template\">", denseStyle);
        Assert.DoesNotContain("<primitives:ListViewItemPresenter", workspace);

        foreach (var property in new[]
        {
            "SelectionCheckMarkVisualEnabled",
            "CheckBrush",
            "CheckBoxBrush",
            "FocusBorderBrush",
            "FocusSecondaryBorderBrush",
            "PointerOverBackground",
            "PointerOverForeground",
            "SelectedBackground",
            "SelectedForeground",
            "SelectedPointerOverBackground",
            "PressedBackground",
            "SelectedPressedBackground",
            "DisabledOpacity",
            "ContentMargin"
        })
        {
            Assert.DoesNotContain($"{property}=", workspace);
        }
    }

    [Fact]
    public void HistoryTextColumnsKeepSemanticGap()
    {
        var workspace = ReadWorkspace();
        var historyTemplate = Slice(workspace,
            "<DataTemplate x:Key=\"HistoryItemTemplate\"",
            "<DataTemplate x:Key=\"RepositoryTreeItemTemplate\">");

        Assert.Contains("<Thickness x:Key=\"Margin.HistoryColumnGap\">4,0,0,0</Thickness>", workspace);
        Assert.Equal(3, CountOccurrences(historyTemplate, "Margin=\"{StaticResource Margin.HistoryColumnGap}\""));
    }

    private static string ReadWorkspace()
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Styles",
            "Workspace.xaml"));
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {startMarker}");
        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= start, $"Missing marker: {endMarker}");
        return value[start..(end + endMarker.Length)];
    }

    private static int CountOccurrences(string value, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}