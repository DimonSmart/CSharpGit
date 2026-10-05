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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
