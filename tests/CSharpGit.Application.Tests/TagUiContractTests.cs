namespace CSharpGit.Application.Tests;

public sealed class TagUiContractTests
{
    [Fact]
    public void TagWorkflowRemainsContextualSemanticAndSafe()
    {
        var root = FindRepositoryRoot();
        var tags = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Tags.cs"));
        var commitActions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitActions.cs"));
        var branchDeletion = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var refresh = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryRefresh.cs"));
        var descriptors = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeDescriptor.cs"));
        var reconciler = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "IncrementalTreeReconciler.cs"));
        var references = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IReferenceService.cs"));

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
        Assert.Contains("Delete tag…", tags);
        Assert.Contains("Remote tags…", tags);
        Assert.Contains("Copy tag name", tags);
        Assert.Contains("private bool TryShowTagContextMenu(FrameworkElement source, RightTappedRoutedEventArgs args)", tags);
        Assert.Contains("if (TryShowTagContextMenu(source, args)) return;", branchDeletion);
        Assert.DoesNotContain("case RepositoryTreeNodeKind.Tag", branchDeletion, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryTree.RightTapped += RepositoryTree_TagAwareRightTapped", tags, StringComparison.Ordinal);
        Assert.Contains("new MenuFlyoutSubItem { Text = \"Delete tag\" }", tags);
        Assert.Contains("_commitActionsFlyout.Items.IndexOf(_checkoutCommitItem)", tags);
        Assert.Contains("_commitActionsFlyout.Items.Insert(checkoutIndex + 1, _deleteTagSubItem)", tags);
        Assert.Contains("UpdateDeleteTagSubmenu();", tags);
        Assert.Contains("_viewModel.SelectedHistoryRow?.Commit.Hash", tags);
        Assert.Contains("foreach (var tag in _viewModel.Tags)", tags);
        Assert.Contains("string.Equals(tag.TargetCommit, selectedCommitHash, StringComparison.Ordinal)", tags);
        Assert.Contains("item.Click += async (_, _) => await DeleteTagFromUiAsync(tag)", tags);
        Assert.Contains("_deleteTagSubItem.IsEnabled = canMutate && _deleteTagSubItem.Items.Count > 0", tags);

        Assert.Contains("GitTagKind.Annotated", tags);
        Assert.Contains("Annotated tags require a non-empty message", tags);
        Assert.Contains("refs/tags/{tag.Name}", tags);
        Assert.Contains("_tagService.CreateTagAsync", tags);
        Assert.Contains("_tagService.FetchTagsAsync", tags);
        Assert.Contains("_tagService.PushTagAsync", tags);
        Assert.Contains("_tagService.PushAllTagsAsync", tags);
        Assert.Contains("_tagService.DeleteRemoteTagAsync", tags);
        Assert.Contains("_tagService.DeleteTagAsync", tags);
        Assert.Contains("_tagService.ForceUpdateRemoteTagAsync", tags);
        Assert.DoesNotContain("ProcessStartInfo", tags);
        Assert.DoesNotContain("RunGit", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("GitCommandExecutor", tags, StringComparison.Ordinal);

        var deleteTagSubmenuStart = tags.IndexOf("private void UpdateDeleteTagSubmenu()", StringComparison.Ordinal);
        var deleteTagSubmenuEnd = tags.IndexOf("private void ApplyTagOrderingToRepositoryTree()", deleteTagSubmenuStart, StringComparison.Ordinal);
        Assert.True(deleteTagSubmenuStart >= 0 && deleteTagSubmenuEnd > deleteTagSubmenuStart);
        var deleteTagSubmenu = tags[deleteTagSubmenuStart..deleteTagSubmenuEnd];
        Assert.DoesNotContain("DeleteRemoteTagFromUiAsync", deleteTagSubmenu, StringComparison.Ordinal);
        Assert.DoesNotContain("_tagService", deleteTagSubmenu, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", deleteTagSubmenu, StringComparison.Ordinal);

        Assert.Contains("Title = \"Delete tag?\"", tags);
        Assert.Contains("Also delete this tag from remote", tags);
        Assert.Contains("IsChecked = hasRemotes", tags);
        Assert.Contains("Title = \"Delete remote tag?\"", tags);
        Assert.Contains("The local tag will remain", tags);
        Assert.Contains("Title = \"Force update remote tag?\"", tags);
        Assert.Contains("Moving an already published tag may break consumers", tags);
        Assert.True(Count(tags, "DefaultButton = ContentDialogButton.Close") >= 4,
            "Destructive tag actions must default to safe cancellation.");

        Assert.Contains("_viewModel.Remotes.Count == 1", tags);
        Assert.Contains("_tagService.ReadRemoteTagsAsync", tags);
        Assert.Contains("remoteTag.ObjectId, tag.ObjectId", tags);
        Assert.Contains("Select the remote explicitly", tags);
        Assert.Contains("CurrentOperation == RepositoryOperation.None", tags);
        Assert.Contains("RunMutationAsync", tags);
        Assert.Contains("includeHistory: false", tags);

        Assert.Contains("CreateBranchFromReferenceAsync($\"refs/tags/{tag.Name}\", tag.TargetCommit)", tags);
        Assert.Contains("CreateBranchFromReferenceAsync(commit.Hash, commit.Hash)", commitActions);
        Assert.Contains("_commitActionsFlyout.Items.Add(_checkoutCommitItem);", commitActions);
        Assert.Contains("_commitActionsFlyout.Items.Add(new MenuFlyoutSeparator());", commitActions);
        Assert.Contains("Switch to the new branch", commitActions);
        Assert.Contains("switchToBranch.IsChecked == true", commitActions);
        Assert.Contains("_referenceService.CreateBranchAsync(repository, branchName.Text.Trim(), startPoint, switched)", commitActions);

        Assert.Contains("public interface IReferenceService", references);
        Assert.DoesNotContain(": ITagService", references, StringComparison.Ordinal);
        Assert.Contains("ITagService _tagService", mainPage);
        Assert.Contains("ITagService tagService", mainPage);
        Assert.DoesNotContain("OrderByDescending(tag => tag.Name", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("children: _viewModel.Tags", mainPage, StringComparison.Ordinal);
        Assert.Contains("ChildNodes: tags.Select", descriptors, StringComparison.Ordinal);
        Assert.Contains("target.Move(currentIndex, desiredIndex);", reconciler, StringComparison.Ordinal);
        Assert.DoesNotContain("root.Children.Clear();", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyTagOrderingToRepositoryTree();", refresh, StringComparison.Ordinal);
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
