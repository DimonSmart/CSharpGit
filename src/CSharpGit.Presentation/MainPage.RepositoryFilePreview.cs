using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Previewing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly FilePreviewService _repositoryFilePreviewService = new();
    private readonly PreviewPublicationGate<RepositoryPreviewContext> _repositoryPreviewGate = new();
    private FilePreviewHost? _repositoryFilePreviewHost;
    private RepositoryFileSelection? _repositoryFileSelection;

    private Grid BuildRepositoryFilesSplitBody(Grid leftPane)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(2, GridUnitType.Star),
            MinWidth = 180
        });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(3, GridUnitType.Star),
            MinWidth = 250
        });

        Grid.SetColumn(leftPane, 0);
        body.Children.Add(leftPane);

        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            MinimumFirst = 180,
            MinimumSecond = 250,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        _repositoryFilePreviewHost = new FilePreviewHost();
        Grid.SetColumn(_repositoryFilePreviewHost, 2);
        body.Children.Add(_repositoryFilePreviewHost);
        return body;
    }

    private void ApplyRepositoryFileSelection(
        string path,
        int? lineNumber,
        RepositorySnapshotEntry? entry)
    {
        _repositoryFileSelection = new RepositoryFileSelection(path, lineNumber, entry);
        if (_repositoryFilePreviewHost is null) return;

        if (entry is null)
        {
            _repositoryPreviewGate.Cancel();
            _repositoryFilePreviewHost.ShowFolder();
            return;
        }

        switch (entry.Kind)
        {
            case RepositorySnapshotEntryKind.File:
                _ = LoadRepositoryFilePreviewAsync(_repositoryFileSelection);
                break;
            case RepositorySnapshotEntryKind.Symlink:
                _repositoryPreviewGate.Cancel();
                _repositoryFilePreviewHost.ShowUnsupported("Symlink preview is not available.");
                break;
            case RepositorySnapshotEntryKind.Submodule:
                _repositoryPreviewGate.Cancel();
                _repositoryFilePreviewHost.ShowUnsupported("Submodule preview is not available.");
                break;
            default:
                _repositoryPreviewGate.Cancel();
                _repositoryFilePreviewHost.ShowUnsupported("This Git entry type cannot be previewed.");
                break;
        }
    }

    private void ClearRepositoryFileSelection()
    {
        _repositoryFileSelection = null;
        _repositoryPreviewGate.Cancel();
        _repositoryFilePreviewHost?.ShowNothingSelected();
    }

    private void CancelRepositoryFilePreview() => _repositoryPreviewGate.Cancel();

    private async Task LoadRepositoryFilePreviewAsync(RepositoryFileSelection selection)
    {
        var repository = _viewModel.Repository;
        var commitHash = _repositorySnapshotCommit;
        if (repository is null
            || string.IsNullOrWhiteSpace(commitHash)
            || selection.Entry is not { Kind: RepositorySnapshotEntryKind.File } entry
            || _repositorySnapshotService is null
            || _fileVersionService is null
            || !ReferenceEquals(repository, _repositorySnapshotRepository)
            || !string.Equals(commitHash, _viewModel.SelectedHistoryRow?.Commit.Hash, StringComparison.Ordinal))
        {
            return;
        }

        var context = new RepositoryPreviewContext(
            RepositoryIdentity(repository),
            commitHash,
            selection.Path,
            selection.LineNumber);
        var lease = _repositoryPreviewGate.Begin(context);
        _repositoryFilePreviewHost?.ShowLoading(selection.Path);

        try
        {
            var version = await _repositorySnapshotService.ResolveFileVersionAsync(
                repository,
                commitHash,
                entry.Path,
                lease.CancellationToken);
            if (!CanPublishRepositoryPreview(lease)) return;
            if (!version.CanOpen)
            {
                _repositoryFilePreviewHost?.ShowUnsupported(
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
            _repositoryFilePreviewHost?.Show(content);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (CanPublishRepositoryPreview(lease))
                _repositoryFilePreviewHost?.ShowError(UserFacingFileError(exception));
        }
    }

    private bool CanPublishRepositoryPreview(PreviewLease<RepositoryPreviewContext> lease)
    {
        if (!IsRepositoryFilesActive || _repositoryFileSelection is not { } selection) return false;
        var repository = _viewModel.Repository;
        var commitHash = _viewModel.SelectedHistoryRow?.Commit.Hash;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash)) return false;

        var current = new RepositoryPreviewContext(
            RepositoryIdentity(repository),
            commitHash,
            selection.Path,
            selection.LineNumber);
        return _repositoryPreviewGate.CanPublish(lease, current);
    }

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
