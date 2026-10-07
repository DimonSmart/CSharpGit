namespace CSharpGit.Desktop.Tests;

public sealed class PublishBranchUiContractTests
{
    [Fact]
    public void PublishAndPushToShareRepositorySyncTargetWorkflow()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var converter = File.ReadAllText(Path.Combine(presentation, "Controls", "BranchTrackingActionTextConverter.cs"));
        var workflow = File.ReadAllText(Path.Combine(presentation, "MainPage.ForcePush.cs"));
        var feature = File.ReadAllText(Path.Combine(presentation, "ViewModels", "RepositorySyncViewModel.cs"));
        var branches = File.ReadAllText(Path.Combine(presentation, "ViewModels", "BranchesViewModel.cs"));
        var settings = File.ReadAllText(Path.Combine(presentation, "SettingsPage.xaml"));

        Assert.Contains("Publish branch…", converter, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=PushMenu", xaml, StringComparison.Ordinal);
        Assert.Contains("Push to…", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"PushTo_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding RepositorySync.CanPushTo}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("ShowPublishBranchDialogAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ShowPushToDialogAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ShowPushTargetDialogAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RepositorySync.PreparePublishBranchAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RepositorySync.PublishBranchAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = _viewModel.RepositorySync.Remotes", workflow, StringComparison.Ordinal);
        Assert.Contains("preparation.SuggestedRemote", workflow, StringComparison.Ordinal);
        Assert.Contains("Track this remote branch as upstream", workflow, StringComparison.Ordinal);
        Assert.Contains("remoteCombo.SelectedItem is GitRemote", workflow, StringComparison.Ordinal);
        Assert.Contains("PushTargetDialogResult", workflow, StringComparison.Ordinal);

        Assert.Contains("PushOptions(AutoSetupRemote: true)", feature, StringComparison.Ordinal);
        Assert.Contains("_settings.AutoSetupRemoteOnPush", feature, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(currentBranch.Upstream)", feature, StringComparison.Ordinal);
        Assert.Contains("PushResultKind.PushDestinationUnavailable", feature, StringComparison.Ordinal);
        Assert.Contains("PushResultKind.NonFastForwardRejected", feature, StringComparison.Ordinal);
        Assert.Contains("Choose publish target…", workflow, StringComparison.Ordinal);
        Assert.Contains("Automatically set upstream on first push", settings, StringComparison.Ordinal);
        Assert.Contains("push.autoSetupRemote", settings, StringComparison.Ordinal);

        Assert.DoesNotContain("RefreshAfterRemoteOperationAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositorySyncService", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PreparePublishBranchAsync", branches, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishBranchAsync", branches, StringComparison.Ordinal);
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
