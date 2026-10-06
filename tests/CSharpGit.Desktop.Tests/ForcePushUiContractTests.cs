namespace CSharpGit.Desktop.Tests;

public sealed class ForcePushUiContractTests
{
    [Fact]
    public void MainPageExposesSeparateLeaseOnlyForceWorkflow()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var trackingConverter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "BranchTrackingActionTextConverter.cs"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ForcePush.cs"));

        Assert.Contains("ConverterParameter=Push", xaml, StringComparison.Ordinal);
        Assert.Contains("\"Push ▼\"", trackingConverter, StringComparison.Ordinal);
        Assert.Contains("Force push with lease…", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ForcePushWithLease_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PrepareForcePushWithLeaseAsync(repository)", workflow, StringComparison.Ordinal);
        Assert.Contains("PrepareForcePushWithLeaseAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("target.Value.Remote", workflow, StringComparison.Ordinal);
        Assert.Contains("target.Value.RemoteBranch", workflow, StringComparison.Ordinal);
        Assert.Contains("ForcePushWithLeaseAsync(repository, snapshot)", workflow, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Force push with lease\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Force anyway", xaml + workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanForcePushWithLeaseRequiresCurrentBranchButNotUpstream()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "OpenRepositoryViewModel.cs"));

        var property = ExtractUntilSemicolon(viewModel, "public bool CanForcePushWithLease");
        Assert.Contains("Repository is not null", property, StringComparison.Ordinal);
        Assert.Contains("!IsBusy", property, StringComparison.Ordinal);
        Assert.Contains("CurrentOperation == RepositoryOperation.None", property, StringComparison.Ordinal);
        Assert.Contains("Branches.LocalBranches.Any(branch => branch.IsCurrent)", property, StringComparison.Ordinal);
        Assert.DoesNotContain("Upstream", property, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingUpstreamUsesDedicatedExplicitTargetDialog()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ForcePush.cs"));

        var forcePush = ExtractMethod(workflow, "private async Task RunForcePushWithLeaseAsync()");
        var targetDialog = ExtractMethod(workflow, "private async Task<(string Remote, string RemoteBranch)?> ShowForcePushTargetDialogAsync(string localBranch)");
        var clickHandler = ExtractMethod(workflow, "private async void ForcePushWithLease_Click(object sender, RoutedEventArgs e)");

        Assert.Contains("ForcePushPreparationFailure.MissingUpstream", forcePush, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Remotes.Count == 0", forcePush, StringComparison.Ordinal);
        Assert.Contains("No Git remotes are configured for this repository.", forcePush, StringComparison.Ordinal);
        Assert.Contains("ShowForcePushTargetDialogAsync(currentBranch.Name)", forcePush, StringComparison.Ordinal);
        Assert.Contains("PrepareForcePushWithLeaseAsync(", forcePush, StringComparison.Ordinal);
        Assert.Contains("target.Value.Remote", forcePush, StringComparison.Ordinal);
        Assert.Contains("target.Value.RemoteBranch", forcePush, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SelectedRemote", forcePush, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.Push" + "BranchName", forcePush, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SetUpstream", forcePush, StringComparison.Ordinal);
        Assert.DoesNotContain("GitOperations" + "Dialog", forcePush, StringComparison.Ordinal);

        Assert.Contains("Title = \"Force push target\"", targetDialog, StringComparison.Ordinal);
        Assert.Contains("Text = \"Local branch\"", targetDialog, StringComparison.Ordinal);
        Assert.Contains("Text = \"Remote\"", targetDialog, StringComparison.Ordinal);
        Assert.Contains("Text = \"Remote branch\"", targetDialog, StringComparison.Ordinal);
        Assert.Contains("Text = localBranch", targetDialog, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly = true", targetDialog, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = _viewModel.Remotes", targetDialog, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText = \"Select remote\"", targetDialog, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Continue\"", targetDialog, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = \"Cancel\"", targetDialog, StringComparison.Ordinal);
        Assert.Contains("remoteCombo.SelectedItem is GitRemote", targetDialog, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(remoteBranchBox.Text)", targetDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", targetDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SelectedRemote", targetDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.Push" + "BranchName", targetDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SetUpstream", targetDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("SuggestedRemote", targetDialog, StringComparison.Ordinal);

        Assert.DoesNotContain("GitOperations" + "Dialog", clickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", clickHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyOperationsContainerIsRemovedAndPushMenuKeepsContextualActions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var dialogs = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Dialogs.cs"));

        var legacyDialogName = "GitOperations" + "Dialog";
        var legacyMenuLabel = "More Git " + "operations…";
        Assert.DoesNotContain(legacyDialogName, xaml + dialogs, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyMenuLabel, xaml, StringComparison.Ordinal);
        Assert.Contains("Push to…", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"PushTo_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Force push with lease…", xaml, StringComparison.Ordinal);

        var pushTo = xaml.IndexOf("Push to…", StringComparison.Ordinal);
        var separator = xaml.IndexOf("<MenuFlyoutSeparator />", pushTo, StringComparison.Ordinal);
        var forcePush = xaml.IndexOf("Force push with lease…", StringComparison.Ordinal);
        Assert.True(pushTo >= 0 && separator > pushTo && forcePush > separator);
    }

    [Fact]
    public void CanPushToRequiresAttachedBranchRemoteAndIdleRepository()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "OpenRepositoryViewModel.cs"));

        var property = ExtractUntilSemicolon(viewModel, "public bool CanPushTo");
        Assert.Contains("Repository is not null", property, StringComparison.Ordinal);
        Assert.Contains("!IsBusy", property, StringComparison.Ordinal);
        Assert.Contains("CurrentOperation == RepositoryOperation.None", property, StringComparison.Ordinal);
        Assert.Contains("Branches.LocalBranches.Any(branch => branch.IsCurrent)", property, StringComparison.Ordinal);
        Assert.Contains("Remotes.Count > 0", property, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextualReplacementsRemainAvailableAndDeadCreateCommandIsRemoved()
    {
        var root = FindRepositoryRoot();
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "OpenRepositoryViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));

        Assert.Contains("Create branch from here…", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Merge into current branch", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Checkout as tracking branch", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Fetch all", codeBehind, StringComparison.Ordinal);

        Assert.DoesNotContain("CreateBranchCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBranchCommand", viewModel, StringComparison.Ordinal);
    }



    private static string ExtractUntilSemicolon(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker '{marker}' was not found.");
        var end = source.IndexOf(';', start);
        Assert.True(end >= 0, $"Semicolon after '{marker}' was not found.");
        return source[start..(end + 1)];
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method '{signature}' was not found.");
        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart >= 0, $"Method body for '{signature}' was not found.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0) return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Method body for '{signature}' was not closed.");
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
