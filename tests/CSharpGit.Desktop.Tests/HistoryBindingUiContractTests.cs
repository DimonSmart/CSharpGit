namespace CSharpGit.Desktop.Tests;

public sealed class HistoryBindingUiContractTests
{
    [Fact]
    public void HistoryTemplatesUseTypedOneWayBindingsForRecycledImmutableRows()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Styles",
            "HistoryReferences.xaml"));

        Assert.Equal(5, CountOccurrences(xaml, "x:DataType=\"domain:HistoryRow\""));
        Assert.Contains("xmlns:domain=\"using:CSharpGit.Domain\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Commit.Subject, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Commit.Author, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AuthorName=\"{x:Bind Commit.Author, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AuthorEmail=\"{x:Bind Commit.AuthorEmail, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Commit.ShortHash, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{x:Bind Commit.AuthoredAt, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Graph=\"{x:Bind Topology, Mode=OneWay, Converter={StaticResource CommitTopologyToGraphVisualConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind IsReflogOnly, Mode=OneWay, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(xaml, "References=\"{x:Bind ReferenceDetails, Mode=OneWay}\""));

        Assert.DoesNotContain("{Binding Commit.Subject}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Commit.Author}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Commit.AuthorEmail}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Commit.ShortHash}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Topology", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding IsReflogOnly", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding Commit.References}\"", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("Text=\"{Binding Commit.AuthoredAt}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(xaml, "<controls:CommitTimeText "));
        Assert.Equal(4, CountOccurrences(xaml, "Value=\"{x:Bind Commit.AuthoredAt, Mode=OneWay}\""));
    }

    [Fact]
    public void CommitTimeControlsBindToTheirWindowHistoryModeAndReleaseItOnUnload()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitTimeText.cs"));
        var history = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "HistoryView.xaml.cs"));
        var details = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml"));

        Assert.Contains("ICommitTimeModeProvider.CommitTimeModeSource => _history", history, StringComparison.Ordinal);
        Assert.Contains("BindHistoryMode();", control, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetParent(ancestor)", control, StringComparison.Ordinal);
        Assert.Contains("source.PropertyChanged += HistoryModeChanged", control, StringComparison.Ordinal);
        Assert.Contains("nameof(HistoryViewModel.CommitTimeDisplayMode)", control, StringComparison.Ordinal);
        Assert.Contains("Mode = source.CommitTimeDisplayMode", control, StringComparison.Ordinal);
        Assert.Contains("_historyModeSource.PropertyChanged -= HistoryModeChanged", control, StringComparison.Ordinal);
        Assert.Contains("DetachHistoryMode();", control, StringComparison.Ordinal);
        Assert.Contains("DataContextChanged +=", control, StringComparison.Ordinal);
        Assert.Contains("Mode=\"{Binding CommitTimeDisplayMode}\"", details, StringComparison.Ordinal);
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
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
