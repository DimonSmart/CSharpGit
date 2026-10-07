namespace CSharpGit.Application.Tests;

public sealed class TagUiContractTests
{
    [Fact]
    public void TagWorkflowRemainsContextualSemanticAndSafe()
    {
        var root = FindRepositoryRoot();
        var tags = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Tags.cs"));
        var tagFeature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "TagsViewModel.cs"));
        var commitActions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitActions.cs"));
        var branchDeletion = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var refresh = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryRefresh.cs"));
        var descriptors = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeDescriptor.cs"));
        var reconciler = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "IncrementalTreeReconciler.cs"));
        var references = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IReferenceService.cs"));

        foreach (var text in new[]
                 {
                     "Create tag here…", "Create tag…", "Fetch tags…", "Push all tags…", "Tag details…",
                     "Show history up to tag", "Create branch from here…", "Checkout detached", "Push tag…",
                     "Delete from remote…", "Delete tag…", "Remote tags…", "Copy tag name"
                 })
            Assert.Contains(text, tags);

        Assert.Contains("private bool TryShowTagContextMenu(FrameworkElement source, RightTappedRoutedEventArgs args)", tags);
        Assert.Contains("if (TryShowTagContextMenu(source, args)) return;", branchDeletion);
        Assert.DoesNotContain("case RepositoryTreeNodeKind.Tag", branchDeletion, StringComparison.Ordinal);
        Assert.Contains("new MenuFlyoutSubItem { Text = \"Delete tag\" }", tags);
        Assert.Contains("_viewModel.History.SelectedRow?.Commit.Hash", tags);
        Assert.Contains("foreach (var tag in _viewModel.Tags)", tags);
        Assert.Contains("string.Equals(tag.TargetCommit, selectedCommitHash, StringComparison.Ordinal)", tags);

        Assert.Contains("GitTagKind.Annotated", tags);
        Assert.Contains("Annotated tags require a non-empty message", tags);
        Assert.Contains("refs/tags/{tag.Name}", tags);
        Assert.Contains("_tagsViewModel.CreateTagAsync", tags);
        Assert.Contains("_tagsViewModel.FetchTagsAsync", tags);
        Assert.Contains("_tagsViewModel.PushTagAsync", tags);
        Assert.Contains("_tagsViewModel.PushAllTagsAsync", tags);
        Assert.Contains("_tagsViewModel.DeleteRemoteTagAsync", tags);
        Assert.Contains("_tagsViewModel.DeleteTagAsync", tags);
        Assert.Contains("_tagsViewModel.ForceUpdateRemoteTagAsync", tags);
        Assert.DoesNotContain("_tagService", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("ITagService", mainPage, StringComparison.Ordinal);

        foreach (var call in new[]
                 {
                     "_tagService.CreateTagAsync", "_tagService.FetchTagsAsync", "_tagService.PushTagAsync",
                     "_tagService.PushAllTagsAsync", "_tagService.DeleteRemoteTagAsync", "_tagService.DeleteTagAsync",
                     "_tagService.ForceUpdateRemoteTagAsync", "_tagService.ReadRemoteTagAsync", "_tagService.ReadRemoteTagsAsync"
                 })
            Assert.Contains(call, tagFeature);

        Assert.Contains("CurrentOperation == RepositoryOperation.None", tagFeature);
        Assert.Contains("RunTagMutationAsync", tagFeature);
        Assert.Contains("includeHistory: false", tagFeature);
        Assert.Contains("context.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Upstream", tagFeature);
        Assert.Contains("context.Remotes.Count == 1", tagFeature);
        Assert.Contains("remoteTag.ObjectId, tag.ObjectId", tagFeature);

        var deleteStart = tagFeature.IndexOf("public async Task<DeleteTagResult> DeleteTagAsync", StringComparison.Ordinal);
        var deleteEnd = tagFeature.IndexOf("public async Task<PushTagResult?> PushTagAsync", deleteStart, StringComparison.Ordinal);
        Assert.True(deleteStart >= 0 && deleteEnd > deleteStart);
        var deleteWorkflow = tagFeature[deleteStart..deleteEnd];
        Assert.True(
            deleteWorkflow.IndexOf("_tagService.DeleteRemoteTagAsync(repository, remoteTag)", StringComparison.Ordinal) <
            deleteWorkflow.LastIndexOf("_tagService.DeleteTagAsync(repository, tag.Name)", StringComparison.Ordinal));

        Assert.Contains("Title = \"Delete tag?\"", tags);
        Assert.Contains("Also delete this tag from remote", tags);
        Assert.Contains("IsChecked = hasRemotes", tags);
        Assert.Contains("Title = \"Delete remote tag?\"", tags);
        Assert.Contains("The local tag will remain", tags);
        Assert.Contains("Title = \"Force update remote tag?\"", tags);
        Assert.Contains("Moving an already published tag may break consumers", tags);
        Assert.True(Count(tags, "DefaultButton = ContentDialogButton.Close") >= 4,
            "Destructive tag actions must default to safe cancellation.");

        Assert.Contains("Select the remote explicitly", tags);
        Assert.Contains("CreateBranchFromReferenceAsync($\"refs/tags/{tag.Name}\", tag.TargetCommit)", tags);
        Assert.Contains("CreateBranchFromReferenceAsync(commit.Hash, commit.Hash)", commitActions);
        Assert.Contains("_commitActionsFlyout.Items.Add(_checkoutCommitItem);", commitActions);
        Assert.Contains("_viewModel.Branches.CreateBranchAsync(", commitActions);

        Assert.Contains("public interface IReferenceService", references);
        Assert.DoesNotContain(": ITagService", references, StringComparison.Ordinal);
        Assert.Contains("TagsViewModel tagsViewModel", mainPage);
        Assert.DoesNotContain("OrderByDescending(tag => tag.Name", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("children: _viewModel.Tags", mainPage, StringComparison.Ordinal);
        Assert.Contains("ChildNodes: tags.Select", descriptors, StringComparison.Ordinal);
        Assert.Contains("target.Move(currentIndex, desiredIndex);", reconciler, StringComparison.Ordinal);
        Assert.DoesNotContain("root.Children.Clear();", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyTagOrderingToRepositoryTree();", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("GitCommandExecutor", tags, StringComparison.Ordinal);
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
