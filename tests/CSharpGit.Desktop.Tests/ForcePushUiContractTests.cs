namespace CSharpGit.Desktop.Tests;

public sealed class ForcePushUiContractTests
{
    [Fact]
    public void MainPageExposesSeparateLeaseOnlyForceWorkflow()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ForcePush.cs"));

        Assert.Contains("Push ▼", xaml, StringComparison.Ordinal);
        Assert.Contains("Force push with lease…", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ForcePushWithLease_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PrepareForcePushWithLeaseAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ForcePushWithLeaseAsync(repository, snapshot)", workflow, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Force push with lease\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Force anyway", xaml + workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
