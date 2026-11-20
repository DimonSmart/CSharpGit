using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Previewing;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Workflows;

// Owns working-tree image diff cancellation and generation-safe publication.
// File services are resolved lazily because the visual shell initializes before file opening.
internal sealed class WorkingTreeImageDiffWorkflow(
    OpenRepositoryViewModel viewModel,
    Func<FileActionsViewModel> fileActions,
    Func<ImageDiffPreviewService> imageDiffService,
    ImageDiffPreviewHost imageHost,
    Func<bool> isShuttingDown,
    Action<DiffPresentationState> present,
    Action<Exception, string> reportFailure)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<FileActionsViewModel> _fileActions = fileActions;
    private readonly Func<ImageDiffPreviewService> _imageDiffService = imageDiffService;
    private readonly ImageDiffPreviewHost _imageHost = imageHost;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<DiffPresentationState> _present = present;
    private readonly Action<Exception, string> _reportFailure = reportFailure;

    private CancellationTokenSource? _workingTreeImageDiffCts;
    private long _workingTreeImageDiffGeneration;

    internal async Task LoadAsync(
        WorkingTreeChange change,
        WorkingTreeDiffKind kind)
    {
        if (_isShuttingDown()) return;
        Cancel();
        var repository = _viewModel.Repository;
        if (repository is null) return;

        var cancellation = new CancellationTokenSource();
        _workingTreeImageDiffCts = cancellation;
        var generation = ++_workingTreeImageDiffGeneration;
        _present(DiffPresentationState.LoadingImage);

        try
        {
            var imageResult = await _imageDiffService().LoadWorkingTreeAsync(
                repository,
                change,
                kind,
                cancellation.Token);
            if (!IsCurrentWorkingTreeImageRequest(repository, change, kind, generation, cancellation.Token))
                return;

            _fileActions().PublishWorkingTreePreview(
                new WorkingTreeFileActionRequest(repository, change, kind),
                imageResult.Versions);

            if (imageResult.Content is null)
            {
                _present(DiffPresentationState.OtherBinary);
                return;
            }

            _imageHost.Show(imageResult.Content);
            _present(DiffPresentationState.Image);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _reportFailure(exception, "Working-tree image diff");
            if (IsCurrentWorkingTreeImageRequest(repository, change, kind, generation, CancellationToken.None))
                _present(DiffPresentationState.Unavailable);
        }
        finally
        {
            if (ReferenceEquals(_workingTreeImageDiffCts, cancellation))
            {
                _workingTreeImageDiffCts = null;
                cancellation.Dispose();
            }
        }
    }

    private bool IsCurrentWorkingTreeImageRequest(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        long generation,
        CancellationToken cancellationToken) =>
        !_isShuttingDown() &&
        !cancellationToken.IsCancellationRequested &&
        generation == _workingTreeImageDiffGeneration &&
        ReferenceEquals(repository, _viewModel.Repository) &&
        _viewModel.WorkingTree.IsActive(change, kind);

    internal void Cancel()
    {
        _workingTreeImageDiffGeneration++;
        var cancellation = Interlocked.Exchange(ref _workingTreeImageDiffCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
