using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Previewing;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Workflows;

// Owns lazy file preview admission, cancellation and publication against snapshot identity.
internal sealed class RepositoryFilePreviewController(
    OpenRepositoryViewModel viewModel,
    RepositoryFilesViewModel repositoryFiles,
    IRepositoryFileVersionService fileVersionService,
    Func<FilePreviewHost?> previewHost,
    Func<bool> isShuttingDown,
    Func<bool> isFilesActive,
    Func<long> activationRevision,
    Action<Func<Task>,string> runUiOperation,
    Action<Exception,string> reportFailure,
    Func<Exception,string> userFacingFileError)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly RepositoryFilesViewModel _repositoryFilesViewModel = repositoryFiles;
    private readonly IRepositoryFileVersionService _fileVersionService = fileVersionService;
    private readonly Func<FilePreviewHost?> _previewHost = previewHost;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<bool> _isFilesActive = isFilesActive;
    private readonly Func<long> _activationRevision = activationRevision;
    private readonly Action<Func<Task>,string> _runUiOperation = runUiOperation;
    private readonly Action<Exception,string> _reportFailure = reportFailure;
    private readonly Func<Exception,string> _userFacingFileError = userFacingFileError;

    private readonly FilePreviewService _repositoryFilePreviewService = new();
    private readonly PreviewPublicationGate<RepositoryPreviewContext> _repositoryPreviewGate = new();
    private RepositoryFileSelection? _pendingRepositoryPreviewSelection;
    internal bool HasPending => _pendingRepositoryPreviewSelection is not null;
    internal long StartedActivation { get; private set; } = -1;

    internal void ApplySelection(
        string path,
        int? lineNumber,
        RepositorySnapshotEntry? entry)
    {
        var host = _previewHost();
        if (_isShuttingDown() || host is null) return;
        _pendingRepositoryPreviewSelection = null;

        if (entry is null)
        {
            _repositoryPreviewGate.Cancel();
            host.ShowFolder();
            return;
        }

        switch (entry.Kind)
        {
            case RepositorySnapshotEntryKind.File:
                _runUiOperation(
                    () => LoadAsync(
                        new RepositoryFileSelection(path, lineNumber, entry)),
                    "Repository file preview");
                break;
            case RepositorySnapshotEntryKind.Symlink:
                _repositoryPreviewGate.Cancel();
                host.ShowUnsupported("Symlink preview is not available.");
                break;
            case RepositorySnapshotEntryKind.Submodule:
                _repositoryPreviewGate.Cancel();
                host.ShowUnsupported("Submodule preview is not available.");
                break;
            default:
                _repositoryPreviewGate.Cancel();
                host.ShowUnsupported("This Git entry type cannot be previewed.");
                break;
        }
    }

    internal void ClearSelection()
    {
        _repositoryPreviewGate.Cancel();
        _previewHost()?.ShowNothingSelected();
    }

    internal void Cancel() => _repositoryPreviewGate.Cancel();

    private async Task LoadAsync(RepositoryFileSelection selection)
    {
        var repository = _repositoryFilesViewModel.CurrentRepository;
        var commitHash = _repositoryFilesViewModel.CurrentCommit;
        if (_isShuttingDown() || repository is null
            || string.IsNullOrWhiteSpace(commitHash)
            || selection.Entry is not { Kind: RepositorySnapshotEntryKind.File } entry
            || _fileVersionService is null
            || !_repositoryFilesViewModel.SnapshotMatchesSelection)
        {
            return;
        }

        using var admission = _viewModel.TryBeginUserGitOperation(repository);
        if (admission is null)
        {
            _pendingRepositoryPreviewSelection = selection;
            _previewHost()?.ShowUnsupported("Waiting for the current Git operation.");
            return;
        }

        _pendingRepositoryPreviewSelection = null;
        var context = new RepositoryPreviewContext(
            RepositoryIdentity(repository),
            commitHash,
            selection.Path,
            selection.LineNumber);
        var lease = _repositoryPreviewGate.Begin(context);
        StartedActivation = _activationRevision();
        _previewHost()?.ShowLoading(selection.Path);

        try
        {
            var version = await _repositoryFilesViewModel.ResolveFileVersionWithinAcceptedOperationAsync(
                entry,
                lease.CancellationToken);
            if (version is null || !CanPublishRepositoryPreview(lease)) return;
            if (!version.CanOpen)
            {
                _previewHost()?.ShowUnsupported(
                    version.UnavailableReason ?? "This historical entry cannot be previewed.");
                return;
            }

            var materialized = await _fileVersionService.MaterializeAsync(
                repository,
                version,
                DiffFileSide.Changed,
                lease.CancellationToken);
            if (!CanPublishRepositoryPreview(lease)) return;

            var content = await _repositoryFilePreviewService.LoadAsync(
                entry.Path,
                materialized.Path,
                lease.CancellationToken);
            if (!CanPublishRepositoryPreview(lease)) return;
            _previewHost()?.Show(content);
        }
        catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested || _isShuttingDown())
        {
        }
        catch (Exception exception)
        {
            _reportFailure(exception, "Repository file preview");
            if (CanPublishRepositoryPreview(lease))
                _previewHost()?.ShowError(_userFacingFileError(exception));
        }
    }

    internal async Task ResumeAsync()
    {
        await Task.Yield();
        var pending = _pendingRepositoryPreviewSelection;
        if (pending is null || _isShuttingDown() || !_isFilesActive()
            || !string.Equals(_repositoryFilesViewModel.SelectedPath, pending.Path, StringComparison.Ordinal)
            || !ReferenceEquals(_repositoryFilesViewModel.SelectedEntry, pending.Entry))
            return;
        _pendingRepositoryPreviewSelection = null;
        await LoadAsync(pending);
    }

    private bool CanPublishRepositoryPreview(PreviewLease<RepositoryPreviewContext> lease)
    {
        if (_isShuttingDown() || !_isFilesActive()
            || _repositoryFilesViewModel.SelectedPath is not { } path)
        {
            return false;
        }

        var repository = _repositoryFilesViewModel.CurrentRepository;
        var commitHash = _repositoryFilesViewModel.CurrentCommit;
        if (_isShuttingDown() || repository is null
            || string.IsNullOrWhiteSpace(commitHash)
            || !_repositoryFilesViewModel.SnapshotMatchesSelection)
        {
            return false;
        }

        var current = new RepositoryPreviewContext(
            RepositoryIdentity(repository),
            commitHash,
            path,
            _repositoryFilesViewModel.SelectedLineNumber);
        return _repositoryPreviewGate.CanPublish(lease, current);
    }

    private static string RepositoryIdentity(Repository repository) =>
        Path.GetFullPath(repository.GitDirectory);

    private sealed record RepositoryFileSelection(
        string Path,
        int? LineNumber,
        RepositorySnapshotEntry? Entry);

    private readonly record struct RepositoryPreviewContext(
        string RepositoryIdentity,
        string CommitHash,
        string Path,
        int? LineNumber);
}
