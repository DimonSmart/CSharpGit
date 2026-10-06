namespace CSharpGit.Desktop.Tests;

public sealed class TagUiContractTests
{
    [Fact]
    public void TagDeletionAndRemoteTagManagementRemainConsistentAndSafe()
    {
        var root = FindRepositoryRoot();
        var tags = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Tags.cs"));
        var tagFeature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "TagsViewModel.cs"));
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
        Assert.Contains("context.Remotes.Count == 1", tagFeature, StringComparison.Ordinal);
        Assert.Contains("branch.IsCurrent)?.Upstream", tagFeature, StringComparison.Ordinal);

        Assert.Contains("_tagService.ReadRemoteTagAsync(repository, remote.Name, tag.Name)", tagFeature, StringComparison.Ordinal);
        Assert.Contains("remoteTag.ObjectId, tag.ObjectId", tagFeature, StringComparison.Ordinal);
        var combinedStart = tagFeature.IndexOf("public async Task<DeleteTagResult> DeleteTagAsync", StringComparison.Ordinal);
        var combinedEnd = tagFeature.IndexOf("public async Task<PushTagResult?> PushTagAsync", combinedStart, StringComparison.Ordinal);
        Assert.True(combinedStart >= 0 && combinedEnd > combinedStart);
        var combined = tagFeature[combinedStart..combinedEnd];
        Assert.True(
            combined.IndexOf("_tagService.DeleteRemoteTagAsync(repository, remoteTag)", StringComparison.Ordinal) <
            combined.LastIndexOf("_tagService.DeleteTagAsync(repository, tag.Name)", StringComparison.Ordinal));
        Assert.Contains("if (remote is null)", combined, StringComparison.Ordinal);

        Assert.Contains("\"Delete from remote…\"", tags, StringComparison.Ordinal);
        Assert.Contains("var lookup = await _tagsViewModel.ReadRemoteTagAsync(remote, tag.Name)", tags, StringComparison.Ordinal);
        Assert.Contains("_tagsViewModel.DeleteRemoteTagAsync(remoteTag)", tags, StringComparison.Ordinal);

        Assert.Contains("\"Remote tags…\"", tags, StringComparison.Ordinal);
        Assert.Contains("_tagsViewModel.ReadRemoteTagsAsync(remote)", tags, StringComparison.Ordinal);
        Assert.Contains("_tagsViewModel.DeleteRemoteTagAsync(selectedTag)", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("_tagService", tags, StringComparison.Ordinal);

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
