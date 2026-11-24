namespace CSharpGit.Desktop.Tests;

public sealed class ImageDiffUiContractTests
{
    [Fact]
    public void CommitAndWorkingTreeUseExplicitImageDiffPresentationStates()
    {
        var root = FindRepositoryRoot();
        var xaml = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml")
            + Read(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeView.xaml")
            + Read(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.xaml");
        var changes = Read(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.Changes.cs");
        var workingTree = Read(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeView.Selection.cs");
        var lifecycle = Read(root, "src", "CSharpGit.Presentation", "Workflows", "CommitImageDiffWorkflow.cs");
        var presentation = Read(root, "src", "CSharpGit.Presentation", "Workflows", "DiffPresentationController.cs");

        Assert.Contains("x:Name=\"CommitDiffHeader\"", xaml);
        Assert.Contains("x:Name=\"CommitTextDiffHeader\"", xaml);
        Assert.Contains("x:Name=\"CommitImageDiffHost\"", xaml);
        Assert.Contains("x:Name=\"WorkingTreeImageDiffHost\"", xaml);
        Assert.Contains("Loading image comparison…", xaml);
        Assert.DoesNotContain("Visibility=\"{Binding HasBinaryDiff", xaml);
        Assert.Contains("DiffPresentationState", presentation);
        Assert.Contains("NoTextualPatch", presentation);
        Assert.Contains("DeltaMissing", presentation);
        Assert.Contains("_commitImageDiff.LoadAsync", changes);
        var workingImage = Read(root, "src", "CSharpGit.Presentation", "Workflows", "WorkingTreeImageDiffWorkflow.cs");
        Assert.Contains("_imageDiffService().LoadWorkingTreeAsync", workingImage);
        Assert.Contains("RenderCommit(", presentation);
        Assert.Contains("_commit.Header.Visibility", presentation);
        Assert.Contains("RenderWorkingTree(", presentation);
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.DiffPresentation.cs")));
    }

    [Fact]
    public void ImageDiffUsesSemanticFileVersionsAndSafeLocalPathResolution()
    {
        var root = FindRepositoryRoot();
        var service = Read(root, "src", "CSharpGit.Presentation", "Previewing", "ImageDiffPreviewService.cs");
        var resolver = Read(root, "src", "CSharpGit.Presentation.Core", "Previewing", "DiffFileVersionPathResolver.cs");
        var fileOpening = Read(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs");

        Assert.Contains("ResolveCommitAsync", service);
        Assert.Contains("ResolveWorkingTreeAsync", service);
        Assert.Contains("DiffFileVersionLocation.WorkingCopy", resolver);
        Assert.Contains("ResolveExistingWorkingTreeFile", resolver);
        Assert.Contains("DiffFileVersionLocation.GitSnapshot", resolver);
        Assert.Contains("MaterializeAsync", resolver);
        var fileActions = Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "FileActionsViewModel.cs");
        var imageUi = Read(root, "src", "CSharpGit.Presentation", "Workflows", "CommitImageDiffWorkflow.cs");
        Assert.Contains("_pathResolver.ResolveAsync", fileActions);
        Assert.Contains("_imageDiffService.LoadCommitAsync(", imageUi);
        Assert.Contains("_fileActions.PathResolver", fileOpening);
        Assert.DoesNotContain("ResolveCommitAsync(", fileOpening);
        Assert.DoesNotContain("git show", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git cat-file", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git diff", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SinglePreviewAndDiffShareMetadataReaderAndBoundedSafety()
    {
        var root = FindRepositoryRoot();
        var filePreview = Read(root, "src", "CSharpGit.Presentation.Core", "Previewing", "FilePreviewService.cs");
        var imageDiff = Read(root, "src", "CSharpGit.Presentation", "Previewing", "ImageDiffPreviewService.cs");
        var metadata = Read(root, "src", "CSharpGit.Presentation.Core", "Previewing", "ImageMetadataReader.cs");

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
        var lifecycle = Read(root, "src", "CSharpGit.Presentation", "Workflows", "CommitImageDiffWorkflow.cs");
        var workingTree = Read(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeView.Selection.cs");

        Assert.Contains("_commitImageDiffGeneration", lifecycle);
        Assert.Contains("IsCurrentCommitImageDiffRequest", lifecycle);
        Assert.Contains("SelectedDiffCommitHash", lifecycle);
        Assert.Contains("SelectedFile?.Path", lifecycle);
        var workingImage = Read(root, "src", "CSharpGit.Presentation", "Workflows", "WorkingTreeImageDiffWorkflow.cs");
        Assert.Contains("_workingTreeImageDiffGeneration", workingImage);
        Assert.Contains("IsCurrentWorkingTreeImageRequest", workingImage);
        Assert.Contains("_present(DiffPresentationState.LoadingImage)", workingImage);
        Assert.Contains("_workingTreeImageDiff.Cancel()", workingTree);
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ImageDiff.cs")));
        Assert.Contains("generation == Volatile.Read", lifecycle);
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
