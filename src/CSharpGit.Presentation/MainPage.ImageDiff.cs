using CSharpGit.Domain;
using CSharpGit.Presentation.Previewing;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ImageDiffPreviewService? _imageDiffPreviewService;
    private CancellationTokenSource? _commitImageDiffCts;
    private long _commitImageDiffGeneration;

    private ImageDiffPreviewService ImageDiffService =>
        _imageDiffPreviewService ??= new ImageDiffPreviewService(
            _fileVersionService,
            _diffFileVersionPathResolver,
            SharedImageMetadataReader.Instance);

    private async Task LoadCommitImageDiffAsync()
    {
        var repository = _viewModel.Repository;
        var commitHash = _viewModel.SelectedDiffCommitHash;
        var file = _viewModel.SelectedFile;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash) || file is null || _viewModel.SelectedDiff?.IsBinary != true)
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
                commitHash,
                file.Path,
                cts.Token);
            if (!IsCurrentCommitImageDiffRequest(repository, commitHash, file.Path, generation, cts))
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
            if (IsCurrentCommitImageDiffRequest(repository, commitHash, file.Path, generation, cts))
                SetCommitDiffPresentationState(DiffPresentationState.Unavailable);
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
        && string.Equals(commitHash, _viewModel.SelectedDiffCommitHash, StringComparison.Ordinal)
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

}
