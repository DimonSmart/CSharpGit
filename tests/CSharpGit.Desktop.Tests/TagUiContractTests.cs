namespace CSharpGit.Desktop.Tests;

public sealed class TagUiContractTests
{
    [Fact]
    public void TagDeletionAndRemoteTagManagementRemainConsistentAndSafe()
    {
        var root = FindRepositoryRoot();
        var tags = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Tags.cs"));
        var tagService = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitTagService.cs"));
        var contract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "ITagService.cs"));

        Assert.Contains("\"Delete tag…\"", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Delete local tag…\"", tags, StringComparison.Ordinal);
        Assert.Contains("item.Click += async (_, _) => await DeleteTagFromUiAsync(tag)", tags, StringComparison.Ordinal);
        Assert.Contains("() => DeleteTagFromUiAsync(tag)", tags, StringComparison.Ordinal);

        Assert.Contains("Content = \"Also delete this tag from remote\"", tags, StringComparison.Ordinal);
        Assert.Contains("IsChecked = hasRemotes", tags, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = hasRemotes", tags, StringComparison.Ordinal);
        Assert.Contains("dialog.IsPrimaryButtonEnabled = !deleteRemoteRequested || remoteSelector.SelectedItem is GitRemote", tags, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", tags, StringComparison.Ordinal);

        Assert.Contains("GetPreferredTagRemote()", tags, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Remotes.Count == 1", tags, StringComparison.Ordinal);
        Assert.Contains("branch.IsCurrent)?.Upstream", tags, StringComparison.Ordinal);

        Assert.Contains("_tagService.ReadRemoteTagAsync(repository, remote.Name, tag.Name)", tags, StringComparison.Ordinal);
        Assert.Contains("remoteTag.ObjectId, tag.ObjectId", tags, StringComparison.Ordinal);
        var combinedStart = tags.IndexOf("private async Task DeleteTagFromUiAsync", StringComparison.Ordinal);
        var combinedEnd = tags.IndexOf("private async Task PushTagFromUiAsync", combinedStart, StringComparison.Ordinal);
        Assert.True(combinedStart >= 0 && combinedEnd > combinedStart);
        var combined = tags[combinedStart..combinedEnd];
        Assert.True(
            combined.IndexOf("_tagService.DeleteRemoteTagAsync(repository, remoteTag)", StringComparison.Ordinal) <
            combined.LastIndexOf("_tagService.DeleteTagAsync(repository, tag.Name)", StringComparison.Ordinal));
        Assert.Contains("if (deleteRemote.IsChecked != true)", combined, StringComparison.Ordinal);
        var localOnlyStart = combined.IndexOf("if (deleteRemote.IsChecked != true)", StringComparison.Ordinal);
        var localOnlyEnd = combined.IndexOf("if (remoteSelector.SelectedItem is not GitRemote remote)", localOnlyStart, StringComparison.Ordinal);
        Assert.True(localOnlyStart >= 0 && localOnlyEnd > localOnlyStart);
        Assert.DoesNotContain("ReadRemoteTagAsync", combined[localOnlyStart..localOnlyEnd], StringComparison.Ordinal);

        Assert.Contains("\"Delete from remote…\"", tags, StringComparison.Ordinal);
        Assert.Contains("RemoteTagInfo? remoteTag", tags, StringComparison.Ordinal);
        Assert.Contains("_tagService.DeleteRemoteTagAsync(repository, remoteTag)", tags, StringComparison.Ordinal);

        Assert.Contains("\"Remote tags…\"", tags, StringComparison.Ordinal);
        Assert.Contains("_tagService.ReadRemoteTagsAsync(repository, remote.Name)", tags, StringComparison.Ordinal);
        var remoteDialogStart = tags.IndexOf("private async Task ShowRemoteTagsAsync", StringComparison.Ordinal);
        var remoteDialogEnd = tags.IndexOf("private async Task FetchTagsFromUiAsync", remoteDialogStart, StringComparison.Ordinal);
        Assert.True(remoteDialogStart >= 0 && remoteDialogEnd > remoteDialogStart);
        var remoteDialog = tags[remoteDialogStart..remoteDialogEnd];
        Assert.DoesNotContain("_viewModel.Tags", remoteDialog, StringComparison.Ordinal);
        Assert.Contains("_tagService.DeleteRemoteTagAsync(repository, selectedTag)", remoteDialog, StringComparison.Ordinal);

        Assert.Contains("Task DeleteRemoteTagAsync(", contract, StringComparison.Ordinal);
        Assert.Contains("RemoteTagInfo expectedTag", contract, StringComparison.Ordinal);
        Assert.Contains("Task<IReadOnlyList<RemoteTagInfo>> ReadRemoteTagsAsync", contract, StringComparison.Ordinal);
        Assert.Contains("--force-with-lease=refs/tags/{name}:{expectedObjectId}", tagService, StringComparison.Ordinal);
        Assert.Contains("ParseRemoteTags", tagService, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("GitCommandExecutor", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("--force-with-lease", tags, StringComparison.Ordinal);
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
