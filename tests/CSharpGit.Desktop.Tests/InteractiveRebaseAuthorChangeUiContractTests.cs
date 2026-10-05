namespace CSharpGit.Desktop.Tests;

public sealed class InteractiveRebaseAuthorChangeUiContractTests
{
    [Fact]
    public void RawInteractiveRebaseExposesChangeAuthorWithoutAddingAnotherEditor()
    {
        var xaml = ReadSource("src/CSharpGit.Presentation/MainPage.xaml");

        Assert.Contains("Change author…", xaml, StringComparison.Ordinal);
        Assert.Contains("ChangeInteractiveRebaseAuthor_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("InteractiveRebaseTodoEditor", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("InteractiveRebasePlan", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeAuthorUsesRawTextSelectionAndOnlyPreparesTodo()
    {
        var editor = ReadSource("src/CSharpGit.Presentation/Controls/InteractiveRebaseTodoEditor.xaml.cs");
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.InteractiveRebaseAuthor.cs");

        Assert.Contains("internal int SelectionStart => Editor.SelectionStart;", editor, StringComparison.Ordinal);
        Assert.Contains("internal int SelectionLength => Editor.SelectionLength;", editor, StringComparison.Ordinal);
        Assert.Contains("AnalyzeInteractiveRebaseAuthorChange", source, StringComparison.Ordinal);
        Assert.Contains("ApplyInteractiveRebaseAuthorChange", source, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RebaseTodoText = result.TodoText;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPreparedInteractiveRebaseAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartInteractiveRebaseTodoAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetModeReadsEffectiveRepositoryIdentityAndPreservesAuthorDateByDefault()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.InteractiveRebaseAuthor.cs");
        var viewModel = ReadSource("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.InteractiveRebase.cs");

        Assert.Contains("ReadInteractiveRebaseAuthorIdentityAsync", source, StringComparison.Ordinal);
        Assert.Contains("identity?.Name.EffectiveValue", source, StringComparison.Ordinal);
        Assert.Contains("identity?.Email.EffectiveValue", source, StringComparison.Ordinal);
        Assert.Contains("IRepositoryIdentityService", ReadSource("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs"), StringComparison.Ordinal);
        Assert.Contains("service.ReadAsync(repository)", viewModel, StringComparison.Ordinal);
        Assert.Contains("Reset author date as well", source, StringComparison.Ordinal);
        Assert.Contains("IsChecked = false", source, StringComparison.Ordinal);
        Assert.Contains("Git identity is incomplete.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogShowsScopeCountsAndHistoryRewriteWarning()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.InteractiveRebaseAuthor.cs");

        Assert.Contains("Selected commit lines", source, StringComparison.Ordinal);
        Assert.Contains("All eligible commits in this rebase", source, StringComparison.Ordinal);
        Assert.Contains("commit rows in squash/fixup groups will be skipped", source, StringComparison.Ordinal);
        Assert.Contains("Changing authors rewrites Git history.", source, StringComparison.Ordinal);
        Assert.Contains("Commit hashes may change from the first modified commit onward.", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CSharpGit.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test output directory.");
    }
}
