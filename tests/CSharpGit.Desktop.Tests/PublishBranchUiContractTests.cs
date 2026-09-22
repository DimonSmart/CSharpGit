namespace CSharpGit.Desktop.Tests;

public sealed class PublishBranchUiContractTests
{
    [Fact]
    public void FirstPushUsesDedicatedPublishWorkflow()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var converter = File.ReadAllText(Path.Combine(presentation, "Controls", "BranchTrackingActionTextConverter.cs"));
        var workflow = File.ReadAllText(Path.Combine(presentation, "MainPage.ForcePush.cs"));
        var settings = File.ReadAllText(Path.Combine(presentation, "SettingsPage.xaml"));

        Assert.Contains("Publish branch…", converter, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=PushMenu", xaml, StringComparison.Ordinal);
        Assert.Contains("PreparePublishBranchAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("PublishBranchAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("PushOptions(AutoSetupRemote: true)", workflow, StringComparison.Ordinal);
        Assert.Contains("Choose publish target…", workflow, StringComparison.Ordinal);
        Assert.Contains("Automatically set upstream on first push", settings, StringComparison.Ordinal);
        Assert.Contains("push.autoSetupRemote", settings, StringComparison.Ordinal);
        Assert.Contains("ExplicitPush_Click", xaml, StringComparison.Ordinal);
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
