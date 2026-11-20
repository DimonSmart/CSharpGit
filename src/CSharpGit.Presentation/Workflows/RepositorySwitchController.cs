using CSharpGit.Presentation.ViewModels;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Per-window owner of repository lifecycle transitions and unsaved-message admission.
internal sealed class RepositorySwitchController(
    OpenRepositoryViewModel viewModel,
    Func<bool> isCloning,
    Func<bool> isCreating,
    Func<bool> isShuttingDown,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Action refreshCollections,
    Action refreshHeader)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<bool> _isCloning = isCloning;
    private readonly Func<bool> _isCreating = isCreating;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Action _refreshCollections = refreshCollections;
    private readonly Action _refreshHeader = refreshHeader;

    internal async Task<bool> TrySwitchAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_isCloning()
            || _isCreating()
            || !_viewModel.CanChangeRepository)
            return false;

        var currentPath = _viewModel.Repository?.WorkingDirectory;
        if (currentPath is not null
            && RecentRepositoriesProjectionBuilder.PathsEqual(currentPath, path))
            return true;

        if (!Directory.Exists(path))
        {
            await ShowUnavailableAsync(path);
            return false;
        }

        var originalRepository = _viewModel.Repository;
        var discardDraft = originalRepository is not null
            && _viewModel.CommitCreation.HasUnappliedCommitMessage;
        if (discardDraft
            && !await ConfirmDiscardAsync(closing: false))
            return false;

        if (_isShuttingDown() || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || !_viewModel.CanChangeRepository)
            return false;
        return await SwitchCoreAsync(path, discardDraft);
    }

    internal async Task<bool> SwitchCoreAsync(
        string path,
        bool discardDraft,
        OpenRepositoryViewModel.UserGitOperationLease? continuation = null)
    {
        if (_isShuttingDown()
            || (continuation is null ? !_viewModel.CanChangeRepository
                : !continuation.IsOwnedBy(_viewModel))) return false;

        bool opened;
        try
        {
            opened = continuation is null
                ? await _viewModel.OpenRepositoryPathAsync(path)
                : await _viewModel.OpenRepositoryPathFromLifecycleAsync(path, continuation);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (!opened || _isShuttingDown()) return false;

        if (discardDraft)
            _viewModel.CommitCreation.CommitMessage = string.Empty;

        _refreshCollections();
        _refreshHeader();
        return true;
    }

    internal async Task<bool> TryCloseAsync()
    {
        if (_isCloning() || _isCreating())
            return false;
        if (_viewModel.Repository is null) return true;
        if (!_viewModel.CanChangeRepository) return false;

        var originalRepository = _viewModel.Repository;
        var discardDraft = _viewModel.CommitCreation.HasUnappliedCommitMessage;
        if (discardDraft
            && !await ConfirmDiscardAsync(closing: true))
            return false;

        return CloseCore(originalRepository, discardDraft);
    }

    // The UI confirmation dialog is owned by TryCloseRepositoryAsync. The
    // core applies the already-approved choice and can be tested separately.
    private bool CloseCore(CSharpGit.Domain.Repository expectedRepository, bool discardDraft)
    {
        if (_isShuttingDown() || !ReferenceEquals(expectedRepository, _viewModel.Repository)
            || !_viewModel.CanChangeRepository || !_viewModel.CloseRepository())
            return false;

        if (discardDraft)
            _viewModel.CommitCreation.CommitMessage = string.Empty;

        _refreshCollections();
        _refreshHeader();
        return true;
    }

    internal Task<bool> ConfirmedSwitchForDesktopCheckAsync(string path) =>
        SwitchCoreAsync(path, discardDraft: true);

    internal bool ConfirmedCloseForDesktopCheck() =>
        _viewModel.Repository is { } repository && CloseCore(repository, discardDraft: true);

    internal async Task<bool> ConfirmDiscardAsync(bool closing)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Discard commit message?",
            Content = closing
                ? "Closing this repository will discard the current commit message."
                : "Opening another repository will discard the current commit message.",
            PrimaryButtonText = closing
                ? "Discard and close"
                : "Discard and continue",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close
        };

        return await _showDialog(dialog) == ContentDialogResult.Primary;
    }
    internal async Task ShowUnavailableAsync(string path)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Repository folder not found",
            Content = $"The folder for this recent repository no longer exists:\n\n{path}\n\nYou can remove it from Recent repositories with the remove button.",
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await _showDialog(dialog);
    }
}
