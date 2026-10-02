using CSharpGit.Domain;
using CSharpGit.Presentation.Previewing;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private enum DiffPresentationState
    {
        None,
        LoadingImage,
        Text,
        Image,
        OtherBinary,
        NoChanges,
        Unavailable
    }

    private ImageDiffPreviewService? _imageDiffPreviewService;
    private CancellationTokenSource? _commitImageDiffCts;
    private long _commitImageDiffGeneration;

    private ImageDiffPreviewService ImageDiffService =>
        _imageDiffPreviewService ??= new ImageDiffPreviewService(
            _fileVersionService,
            _repositoryPathService,
            SharedImageMetadataReader.Instance);

    private async Task LoadCommitImageDiffAsync()
    {
        var repository = _viewModel.Repository;
        var commit = _viewModel.SelectedHistoryRow?.Commit;
        var file = _viewModel.SelectedFile;
        if (repository is null || commit is null || file is null || _viewModel.SelectedDiff?.IsBinary != true)
        {
            SetCommitDiffPresentationState(DiffPresentationState.None);
            return;
        }

        var generation = Interlocked.Increment(ref _commitImageDiffGeneration);
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _commitImageDiffCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        SetCommitDiffPresentationState(DiffPresentationState.LoadingImage);

        try
        {
            var result = await ImageDiffService.LoadCommitAsync(
                repository,
                commit.Hash,
                file.Path,
                cts.Token);
            if (!IsCurrentCommitImageDiffRequest(repository, commit.Hash, file.Path, generation, cts))
                return;

            _commitFileVersions = result.Versions;
            _commitRevealPath = TryResolveReveal(repository, result.Versions.RevealPath);
            UpdateCommitButtons();

            if (result.Content is null)
            {
                SetCommitDiffPresentationState(DiffPresentationState.OtherBinary);
                return;
            }

            CommitImageDiffHost.Show(result.Content);
            SetCommitDiffPresentationState(DiffPresentationState.Image);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (IsCurrentCommitImageDiffRequest(repository, commit.Hash, file.Path, generation, cts))
                SetCommitDiffPresentationState(DiffPresentationState.OtherBinary);
        }
        finally
        {
            if (ReferenceEquals(_commitImageDiffCts, cts))
            {
                _commitImageDiffCts = null;
                cts.Dispose();
            }
        }
    }

    private bool IsCurrentCommitImageDiffRequest(
        Repository repository,
        string commitHash,
        string path,
        long generation,
        CancellationTokenSource cancellation) =>
        !cancellation.IsCancellationRequested
        && generation == Volatile.Read(ref _commitImageDiffGeneration)
        && _viewModel.IsChangesViewActive
        && ReferenceEquals(repository, _viewModel.Repository)
        && string.Equals(commitHash, _viewModel.SelectedHistoryRow?.Commit.Hash, StringComparison.Ordinal)
        && string.Equals(path, _viewModel.SelectedFile?.Path, StringComparison.Ordinal)
        && _viewModel.SelectedDiff?.IsBinary == true;

    private void CancelCommitImageDiff(bool clearSurface)
    {
        Interlocked.Increment(ref _commitImageDiffGeneration);
        var cancellation = Interlocked.Exchange(ref _commitImageDiffCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (clearSurface)
            SetCommitDiffPresentationState(DiffPresentationState.None);
    }

    private void SetCommitDiffPresentationState(DiffPresentationState state)
    {
        CommitTextDiffHeader.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        CompactDiffViewer.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        CommitImageLoading.Visibility = state == DiffPresentationState.LoadingImage ? Visibility.Visible : Visibility.Collapsed;
        CommitImageDiffHost.Visibility = state == DiffPresentationState.Image ? Visibility.Visible : Visibility.Collapsed;
        CommitBinaryInfo.Visibility =
            state is DiffPresentationState.OtherBinary or DiffPresentationState.Unavailable
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (state == DiffPresentationState.OtherBinary)
        {
            CommitBinaryInfo.Title = "Binary file";
            CommitBinaryInfo.Message = "Binary file — text diff is not available.";
        }
        else if (state == DiffPresentationState.Unavailable)
        {
            CommitBinaryInfo.Title = "Preview unavailable";
            CommitBinaryInfo.Message = "The selected diff could not be previewed.";
        }

        if (state != DiffPresentationState.Image)
            CommitImageDiffHost.Clear();
    }

    private void SetWorkingTreeDiffPresentationState(DiffPresentationState state)
    {
        WorkingTreeTextDiffHeader.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeDiffViewer.Visibility = state == DiffPresentationState.Text ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeImageLoading.Visibility = state == DiffPresentationState.LoadingImage ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeImageDiffHost.Visibility = state == DiffPresentationState.Image ? Visibility.Visible : Visibility.Collapsed;
        WorkingTreeBinaryInfo.Visibility =
            state is DiffPresentationState.OtherBinary or DiffPresentationState.Unavailable
                ? Visibility.Visible
                : Visibility.Collapsed;
        WorkingTreeNoChangesInfo.Visibility = state == DiffPresentationState.NoChanges ? Visibility.Visible : Visibility.Collapsed;

        if (state == DiffPresentationState.OtherBinary)
        {
            WorkingTreeBinaryInfo.Title = "Binary file";
            WorkingTreeBinaryInfo.Message = "Binary file — text diff is not available.";
        }
        else if (state == DiffPresentationState.Unavailable)
        {
            WorkingTreeBinaryInfo.Title = "Preview unavailable";
            WorkingTreeBinaryInfo.Message = "The selected diff could not be previewed.";
        }

        if (state != DiffPresentationState.Image)
            WorkingTreeImageDiffHost.Clear();
    }
}
