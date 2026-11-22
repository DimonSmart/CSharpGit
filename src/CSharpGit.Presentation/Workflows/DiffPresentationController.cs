using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns text, image and binary diff presentation for the two independent view surfaces.
internal sealed class DiffPresentationController(
    OpenRepositoryViewModel viewModel,
    DiffPresentationController.CommitControls commit,
    DiffPresentationController.WorkingTreeControls workingTree)
{
    internal sealed record CommitControls(
        FrameworkElement Header,
        FrameworkElement TextHeader,
        FrameworkElement TextViewer,
        FrameworkElement LargeInfo,
        TextBlock LargeMessage,
        Button LargeButton,
        FrameworkElement ImageLoading,
        ImageDiffPreviewHost ImageHost,
        InfoBar BinaryInfo);

    internal sealed record WorkingTreeControls(
        FrameworkElement TextHeader,
        FrameworkElement TextViewer,
        FrameworkElement DiffLoading,
        FrameworkElement LargeInfo,
        TextBlock LargeMessage,
        Button LargeButton,
        FrameworkElement ImageLoading,
        ImageDiffPreviewHost ImageHost,
        InfoBar BinaryInfo,
        FrameworkElement NoChangesInfo);

    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly CommitControls _commit = commit;
    private readonly WorkingTreeControls _workingTree = workingTree;

    internal void RenderCommit(DiffPresentationState state)
    {
        _commit.Header.Visibility =
            state is DiffPresentationState.LoadingImage
                or DiffPresentationState.Text
                or DiffPresentationState.Image
                or DiffPresentationState.OtherBinary
                or DiffPresentationState.NoTextualPatch
                or DiffPresentationState.NoNetStashDiff
                or DiffPresentationState.Unavailable
                or DiffPresentationState.Error
                ? Visibility.Visible
                : Visibility.Collapsed;
        _commit.TextHeader.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        _commit.TextViewer.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        _commit.LargeInfo.Visibility = state == DiffPresentationState.LargeDiff ? Visibility.Visible : Visibility.Collapsed;
        _commit.LargeMessage.Text = _viewModel.CommitDetails.DiffPreviewDeferredMessage ?? string.Empty;
        _commit.LargeButton.Content = _viewModel.CommitDetails.DiffPreviewActionText;
        _commit.ImageLoading.Visibility = state == DiffPresentationState.LoadingImage ? Visibility.Visible : Visibility.Collapsed;
        _commit.ImageHost.Visibility = state == DiffPresentationState.Image ? Visibility.Visible : Visibility.Collapsed;
        _commit.BinaryInfo.Visibility =
            state is DiffPresentationState.NothingSelected
                or DiffPresentationState.OtherBinary
                or DiffPresentationState.NoTextualPatch
                or DiffPresentationState.NoNetStashDiff
                or DiffPresentationState.Unavailable
                or DiffPresentationState.Error
                ? Visibility.Visible
                : Visibility.Collapsed;

        ConfigureInfoBar(_commit.BinaryInfo, state, _viewModel.CommitDetails.DiffLoadErrorMessage);

        if (state != DiffPresentationState.Image)
            _commit.ImageHost.Clear();
    }

    internal void RenderWorkingTree(DiffPresentationState state)
    {
        _workingTree.TextHeader.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        _workingTree.TextViewer.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        _workingTree.DiffLoading.Visibility = state == DiffPresentationState.LoadingDiff ? Visibility.Visible : Visibility.Collapsed;
        _workingTree.LargeInfo.Visibility = state == DiffPresentationState.LargeDiff ? Visibility.Visible : Visibility.Collapsed;
        _workingTree.LargeMessage.Text = _viewModel.WorkingTree.DiffPreviewDeferredMessage ?? string.Empty;
        _workingTree.LargeButton.Content = _viewModel.WorkingTree.DiffPreviewActionText;
        _workingTree.ImageLoading.Visibility = state == DiffPresentationState.LoadingImage ? Visibility.Visible : Visibility.Collapsed;
        _workingTree.ImageHost.Visibility = state == DiffPresentationState.Image ? Visibility.Visible : Visibility.Collapsed;
        _workingTree.BinaryInfo.Visibility =
            state is DiffPresentationState.NothingSelected
                or DiffPresentationState.OtherBinary
                or DiffPresentationState.NoTextualPatch
                or DiffPresentationState.Unavailable
                or DiffPresentationState.Error
                ? Visibility.Visible
                : Visibility.Collapsed;
        _workingTree.NoChangesInfo.Visibility = state == DiffPresentationState.DeltaMissing
            ? Visibility.Visible
            : Visibility.Collapsed;

        ConfigureInfoBar(_workingTree.BinaryInfo, state, _viewModel.WorkingTree.DiffLoadErrorMessage);

        if (state != DiffPresentationState.Image)
            _workingTree.ImageHost.Clear();
    }

    private static void ConfigureInfoBar(
        Microsoft.UI.Xaml.Controls.InfoBar infoBar,
        DiffPresentationState state,
        string? errorMessage)
    {
        switch (state)
        {
            case DiffPresentationState.NothingSelected:
                infoBar.Title = "Diff preview";
                infoBar.Message = "Select a changed file to view its diff.";
                break;
            case DiffPresentationState.OtherBinary:
                infoBar.Title = "Binary file";
                infoBar.Message = "Binary file — text diff is not available.";
                break;
            case DiffPresentationState.NoTextualPatch:
                infoBar.Title = "No textual diff";
                infoBar.Message =
                    "Git reports this file as changed, but there is no textual patch to display. " +
                    "This may be caused by normalization, Git attributes, filters, metadata-only changes, " +
                    "or another change that is not represented as a normal text hunk.";
                break;
            case DiffPresentationState.NoNetStashDiff:
                infoBar.Title = "No net working-tree diff";
                infoBar.Message =
                    "The staged and unstaged changes for this file cancel each other in the combined stash content.";
                break;
            case DiffPresentationState.Unavailable:
                infoBar.Title = "Preview unavailable";
                infoBar.Message = "The selected diff could not be previewed.";
                break;
            case DiffPresentationState.Error:
                infoBar.Title = "Could not load diff";
                infoBar.Message = string.IsNullOrWhiteSpace(errorMessage)
                    ? "The selected diff could not be loaded."
                    : errorMessage;
                break;
        }
    }
}
