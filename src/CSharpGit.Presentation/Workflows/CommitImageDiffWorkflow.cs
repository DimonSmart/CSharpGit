using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Previewing;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Workflows;

// Owns image diff cancellation, Git admission and selection/generation-safe publication.
internal sealed class CommitImageDiffWorkflow(
    OpenRepositoryViewModel viewModel,
    FileActionsViewModel fileActions,
    ImageDiffPreviewService imageDiffService,
    ImageDiffPreviewHost imageHost,
    Func<bool> isShuttingDown,
    Action<DiffPresentationState> present,
    Action<Exception, string> reportFailure)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly FileActionsViewModel _fileActions = fileActions;
    private readonly ImageDiffPreviewService _imageDiffService = imageDiffService;
    private readonly ImageDiffPreviewHost _imageHost = imageHost;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<DiffPresentationState> _present = present;
    private readonly Action<Exception, string> _reportFailure = reportFailure;

    private CancellationTokenSource? _commitImageDiffCts;
    private long _commitImageDiffGeneration;
    private bool _pendingCommitImageDiff;
    internal bool HasPending => _pendingCommitImageDiff;

    internal async Task LoadAsync()
    {
        var repository = _viewModel.Repository;
        var commitHash = _viewModel.CommitDetails.SelectedDiffCommitHash;
        var file = _viewModel.CommitDetails.SelectedFile;
        if (_isShuttingDown() || repository is null || string.IsNullOrWhiteSpace(commitHash) || file is null || _viewModel.CommitDetails.SelectedDiff?.IsBinary != true)
        {
            _present(DiffPresentationState.None);
            return;
        }

        using var admission = _viewModel.TryBeginUserGitOperation(repository);
        if (admission is null)
        {
            _pendingCommitImageDiff = true;
            _present(DiffPresentationState.Unavailable);
            return;
        }
        _pendingCommitImageDiff = false;

        var generation = Interlocked.Increment(ref _commitImageDiffGeneration);
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _commitImageDiffCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _present(DiffPresentationState.LoadingImage);

        try
        {
            var result = await _imageDiffService.LoadCommitAsync(
                repository,
                commitHash,
                file.Path,
                cts.Token);
            if (!IsCurrentCommitImageDiffRequest(repository, commitHash, file.Path, generation, cts))
                return;

            _fileActions.PublishCommitPreview(
                new CommitFileActionRequest(repository, commitHash, file.Path),
                result.Versions);

            if (result.Content is null)
            {
                _present(DiffPresentationState.OtherBinary);
                return;
            }

            _imageHost.Show(result.Content);
            _present(DiffPresentationState.Image);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _reportFailure(exception, "Commit image diff");
            if (IsCurrentCommitImageDiffRequest(repository, commitHash, file.Path, generation, cts))
                _present(DiffPresentationState.Unavailable);
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

    internal async Task ResumeAsync()
    {
        await Task.Yield();
        if (_pendingCommitImageDiff && !_isShuttingDown())
            await LoadAsync();
    }

    private bool IsCurrentCommitImageDiffRequest(
        Repository repository,
        string commitHash,
        string path,
        long generation,
        CancellationTokenSource cancellation) =>
        !_isShuttingDown()
        && !cancellation.IsCancellationRequested
        && generation == Volatile.Read(ref _commitImageDiffGeneration)
        && _viewModel.CommitDetails.IsChangesViewActive
        && ReferenceEquals(repository, _viewModel.Repository)
        && string.Equals(commitHash, _viewModel.CommitDetails.SelectedDiffCommitHash, StringComparison.Ordinal)
        && string.Equals(path, _viewModel.CommitDetails.SelectedFile?.Path, StringComparison.Ordinal)
        && _viewModel.CommitDetails.SelectedDiff?.IsBinary == true;

    internal void Cancel(bool clearSurface)
    {
        _pendingCommitImageDiff = false;
        Interlocked.Increment(ref _commitImageDiffGeneration);
        var cancellation = Interlocked.Exchange(ref _commitImageDiffCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (clearSurface)
            _present(DiffPresentationState.None);
    }
}
