using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private enum DiffPresentationState
    {
        None,
        NothingSelected,
        LoadingDiff,
        LargeDiff,
        LoadingImage,
        Text,
        Image,
        OtherBinary,
        NoTextualPatch,
        NoNetStashDiff,
        DeltaMissing,
        Unavailable,
        Error
    }


    private void SetCommitDiffPresentationState(DiffPresentationState state)
    {
        CommitDiffHeader.Visibility =
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
        CommitTextDiffHeader.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        CompactDiffViewer.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        CommitLargeDiffInfo.Visibility = state == DiffPresentationState.LargeDiff ? Visibility.Visible : Visibility.Collapsed;
        CommitLargeDiffMessage.Text = _viewModel.CommitDetails.DiffPreviewDeferredMessage ?? string.Empty;
        CommitLargeDiffButton.Content = _viewModel.CommitDetails.DiffPreviewActionText;
        CommitImageLoading.Visibility = state == DiffPresentationState.LoadingImage ? Visibility.Visible : Visibility.Collapsed;
        CommitImageDiffHost.Visibility = state == DiffPresentationState.Image ? Visibility.Visible : Visibility.Collapsed;
        CommitBinaryInfo.Visibility =
            state is DiffPresentationState.NothingSelected
                or DiffPresentationState.OtherBinary
                or DiffPresentationState.NoTextualPatch
                or DiffPresentationState.NoNetStashDiff
                or DiffPresentationState.Unavailable
                or DiffPresentationState.Error
                ? Visibility.Visible
                : Visibility.Collapsed;

        ConfigureInfoBar(CommitBinaryInfo, state, _viewModel.CommitDetails.DiffLoadErrorMessage);

        if (state != DiffPresentationState.Image)
            CommitImageDiffHost.Clear();
    }

    private void SetWorkingTreeDiffPresentationState(DiffPresentationState state)
    {
        WorkingTreeTextDiffHeader.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeDiffViewer.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeDiffLoading.Visibility = state == DiffPresentationState.LoadingDiff ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeLargeDiffInfo.Visibility = state == DiffPresentationState.LargeDiff ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeLargeDiffMessage.Text = _viewModel.WorkingTree.DiffPreviewDeferredMessage ?? string.Empty;
        WorkingTreeLargeDiffButton.Content = _viewModel.WorkingTree.DiffPreviewActionText;
        WorkingTreeImageLoading.Visibility = state == DiffPresentationState.LoadingImage ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeImageDiffHost.Visibility = state == DiffPresentationState.Image ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeBinaryInfo.Visibility =
            state is DiffPresentationState.NothingSelected
                or DiffPresentationState.OtherBinary
                or DiffPresentationState.NoTextualPatch
                or DiffPresentationState.Unavailable
                or DiffPresentationState.Error
                ? Visibility.Visible
                : Visibility.Collapsed;
        WorkingTreeNoChangesInfo.Visibility = state == DiffPresentationState.DeltaMissing
            ? Visibility.Visible
            : Visibility.Collapsed;

        ConfigureInfoBar(WorkingTreeBinaryInfo, state, _viewModel.WorkingTree.DiffLoadErrorMessage);

        if (state != DiffPresentationState.Image)
            WorkingTreeImageDiffHost.Clear();
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
