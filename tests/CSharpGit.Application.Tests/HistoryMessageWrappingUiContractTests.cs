namespace CSharpGit.Application.Tests;

public sealed class HistoryMessageWrappingUiContractTests
{
    [Fact]
    public void HistorySubjectUsesAvailableMessageWidthAndWrapsInsteadOfTrimming()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Styles",
            "HistoryReferences.xaml"));

        Assert.Contains("<Grid Grid.Column=\"1\" ColumnDefinitions=\"*,Auto,Auto\" MinWidth=\"120\">", xaml);

        var subjectStart = xaml.IndexOf("<TextBlock Text=\"{Binding Commit.Subject}\"", StringComparison.Ordinal);
        Assert.True(subjectStart >= 0);
        var subjectEnd = xaml.IndexOf("/>", subjectStart, StringComparison.Ordinal);
        Assert.True(subjectEnd > subjectStart);
        var subject = xaml[subjectStart..(subjectEnd + 2)];

        Assert.Contains("TextWrapping=\"Wrap\"", subject);
        Assert.DoesNotContain("TextTrimming=", subject);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
