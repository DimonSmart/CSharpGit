namespace CSharpGit.Application.Tests;

public sealed class TagUiContractTests
{
    [Fact]
    public void TagWorkflowRemainsContextualSemanticAndSafe()
    {
        var root = FindRepositoryRoot();
        var tags = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Tags.cs"));
        var commitActions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitActions.cs"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var refresh = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryRefresh.cs"));
        var references = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IRepositoryStateService.cs"));

        Assert.Contains("Create tag here…", tags);
        Assert.Contains("Create tag…", tags);
        Assert.Contains("Fetch tags…", tags);
        Assert.Contains("Push all tags…", tags);
        Assert.Contains("Tag details…", tags);
        Assert.Contains("Show history up to tag", tags);
        Assert.Contains("Create branch from here…", tags);
        Assert.Contains("Checkout detached", tags);
        Assert.Contains("Push tag…", tags);
        Assert.Contains("Delete from remote…", tags);
        Assert.Contains("Delete local tag…", tags);
        Assert.Contains("Copy tag name", tags);

        Assert.Contains("GitTagKind.Annotated", tags);
        Assert.Contains("Annotated tags require a non-empty message", tags);
        Assert.Contains("refs/tags/{tag.Name}", tags);
        Assert.Contains("_referenceService.CreateTagAsync", tags);
        Assert.Contains("_referenceService.FetchTagsAsync", tags);
        Assert.Contains("_referenceService.PushTagAsync", tags);
        Assert.Contains("_referenceService.PushAllTagsAsync", tags);
        Assert.Contains("_referenceService.DeleteRemoteTagAsync", tags);
        Assert.Contains("_referenceService.DeleteTagAsync", tags);
        Assert.Contains("_referenceService.ForceUpdateRemoteTagAsync", tags);
        Assert.DoesNotContain("ProcessStartInfo", tags);
        Assert.DoesNotContain("RunGit", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("GitCommandExecutor", tags, StringComparison.Ordinal);

        Assert.Contains("Title = \"Delete local tag?\"", tags);
        Assert.Contains("This deletes only the local tag", tags);
        Assert.Contains("Remote tags are not deleted", tags);
        Assert.Contains("Title = \"Delete remote tag?\"", tags);
        Assert.Contains("The local tag will remain", tags);
        Assert.Contains("Title = \"Force update remote tag?\"", tags);
        Assert.Contains("Moving an already published tag may break consumers", tags);
        Assert.True(Count(tags, "DefaultButton = ContentDialogButton.Close") >= 4,
            "Destructive tag actions must default to safe cancellation.");

        Assert.Contains("_viewModel.Remotes.Count == 1", tags);
        Assert.Contains("Select the remote explicitly", tags);
        Assert.Contains("CurrentOperation == RepositoryOperation.None", tags);
        Assert.Contains("RunMutationAsync", tags);
        Assert.Contains("includeHistory: false", tags);

        Assert.Contains("CreateBranchFromReferenceAsync($\"refs/tags/{tag.Name}\", tag.TargetCommit)", tags);
        Assert.Contains("CreateBranchFromReferenceAsync(commit.Hash, commit.Hash)", commitActions);
        Assert.Contains("Switch to the new branch", commitActions);
        Assert.Contains("switchToBranch.IsChecked == true", commitActions);
        Assert.Contains("_referenceService.CreateBranchAsync(repository, branchName.Text.Trim(), startPoint, switched)", commitActions);

        Assert.Contains("IReferenceService : ITagService", references);
        Assert.DoesNotContain("OrderByDescending(tag => tag.Name", mainPage, StringComparison.Ordinal);
        Assert.Contains("children: _viewModel.Tags", mainPage);
        Assert.Contains("ApplyTagOrderingToRepositoryTree();", refresh);
    }

    [Fact]
    public void TagModelCarriesAnnotatedMetadataWithoutInventingLightweightDates()
    {
        var root = FindRepositoryRoot();
        var model = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "RepositoryState.cs"));
        var operations = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "TagOperations.cs"));

        foreach (var member in new[]
                 {
                     "TargetCommit", "GitTagKind Kind", "TagObjectId", "TaggerName", "TaggerEmail", "TaggedAt", "Message"
                 }) Assert.Contains(member, model);
        Assert.Contains("RemoteTagConflictSnapshot", operations);
        Assert.Contains("CurrentRemoteObjectId", operations);
        Assert.Contains("NewLocalObjectId", operations);
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
