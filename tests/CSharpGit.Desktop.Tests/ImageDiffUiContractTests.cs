namespace CSharpGit.Desktop.Tests;

public sealed class ImageDiffUiContractTests
{
    [Fact]
    public void CommitAndWorkingTreeUseExplicitImageDiffPresentationStates()
    {
        var root = FindRepositoryRoot();
        var xaml = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var changes = Read(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs");
        var workingTree = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeDiff.cs");
        var lifecycle = Read(root, "src", "CSharpGit.Presentation", "MainPage.ImageDiff.cs");

        Assert.Contains("x:Name=\"CommitImageDiffHost\"", xaml);
        Assert.Contains("x:Name=\"WorkingTreeImageDiffHost\"", xaml);
        Assert.Contains("Loading image comparison…", xaml);
        Assert.DoesNotContain("Visibility=\"{Binding HasBinaryDiff", xaml);
        Assert.Contains("DiffPresentationState", lifecycle);
        Assert.Contains("LoadCommitImageDiffAsync", changes);
        Assert.Contains("ImageDiffService.LoadWorkingTreeAsync", workingTree);
        Assert.Contains("SetCommitDiffPresentationState", lifecycle);
        Assert.Contains("SetWorkingTreeDiffPresentationState", lifecycle);
    }

    [Fact]
    public void ImageDiffUsesSemanticFileVersionsAndSafeLocalPathResolution()
    {
        var root = FindRepositoryRoot();
        var service = Read(root, "src", "CSharpGit.Presentation", "Previewing", "ImageDiffPreviewService.cs");
        var resolver = Read(root, "src", "CSharpGit.Presentation", "Previewing", "DiffFileVersionPathResolver.cs");

        Assert.Contains("ResolveCommitAsync", service);
        Assert.Contains("ResolveWorkingTreeAsync", service);
        Assert.Contains("DiffFileVersionLocation.WorkingCopy", resolver);
        Assert.Contains("ResolveExistingWorkingTreeFile", resolver);
        Assert.Contains("DiffFileVersionLocation.GitSnapshot", resolver);
        Assert.Contains("MaterializeAsync", resolver);
        Assert.DoesNotContain("git show", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git cat-file", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git diff", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SinglePreviewAndDiffShareMetadataReaderAndBoundedSafety()
    {
        var root = FindRepositoryRoot();
        var filePreview = Read(root, "src", "CSharpGit.Presentation", "Previewing", "FilePreviewService.cs");
        var imageDiff = Read(root, "src", "CSharpGit.Presentation", "Previewing", "ImageDiffPreviewService.cs");
        var metadata = Read(root, "src", "CSharpGit.Presentation", "Previewing", "ImageMetadataReader.cs");

        Assert.Contains("SharedImageMetadataReader.Instance", filePreview);
        Assert.Contains("SharedImageMetadataReader.Instance", imageDiff);
        Assert.Contains("MetadataScanBytes", metadata);
        Assert.Contains("MaxDecodedPixels", metadata);
        Assert.Contains("IsPixelCountWithinLimit", metadata);
    }

    [Fact]
    public void ImageDiffPublicationIsSelectionAndGenerationSafe()
    {
        var root = FindRepositoryRoot();
        var lifecycle = Read(root, "src", "CSharpGit.Presentation", "MainPage.ImageDiff.cs");
        var workingTree = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeDiff.cs");

        Assert.Contains("_commitImageDiffGeneration", lifecycle);
        Assert.Contains("IsCurrentCommitImageDiffRequest", lifecycle);
        Assert.Contains("SelectedHistoryRow?.Commit.Hash", lifecycle);
        Assert.Contains("SelectedFile?.Path", lifecycle);
        Assert.Contains("_workingTreeDiffGeneration", workingTree);
        Assert.Contains("IsCurrentWorkingTreeDiffRequest", workingTree);
        Assert.Contains("SetWorkingTreeDiffPresentationState(DiffPresentationState.LoadingImage)", workingTree);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
