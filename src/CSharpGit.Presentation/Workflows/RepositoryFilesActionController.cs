using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns historical repository-file commands and menus. Selection/search stay in RepositoryFilesSurface.
internal sealed class RepositoryFilesActionController(
    OpenRepositoryViewModel viewModel,
    RepositoryFilesViewModel files,
    IRepositoryFileVersionService fileVersionService,
    IExternalGitToolService externalGitToolService,
    FileActionsViewModel fileActions,
    HistoryRewriteController historyRewrite,
    SettingsWindowController settings,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<string, string, Task> showError,
    Func<string, Task> copyText)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly RepositoryFilesViewModel _files = files;
    private readonly IRepositoryFileVersionService _fileVersionService = fileVersionService;
    private readonly IExternalGitToolService _externalGitToolService = externalGitToolService;
    private readonly FileActionsViewModel _fileActions = fileActions;
    private readonly HistoryRewriteController _historyRewrite = historyRewrite;
    private readonly SettingsWindowController _settings = settings;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Func<string, Task> _copyText = copyText;

    internal void ShowMenu(FrameworkElement target, RepositorySnapshotEntry entry)
    {
        var flyout = new MenuFlyout();
        if (entry.Kind == RepositorySnapshotEntryKind.File)
        {
            AddMenuItem(flyout, "Open", () => _viewModel.CanStartUserGitOperation,
                () => OpenAsync(entry, openInEditor: false));
            AddMenuItem(flyout, "Open in editor", () => _viewModel.CanStartUserGitOperation,
                () => OpenAsync(entry, openInEditor: true));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Remove from repository history…",
                () => !_viewModel.IsBusy && !_viewModel.RepositoryHistoryRewrite.IsInProgress,
                () => _historyRewrite.RemovePathAsync(entry));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddMenuItem(flyout, "Copy path", () => true, () => _copyText(entry.Path));
        flyout.ShowAt(target);
    }

    internal async Task OpenAsync(RepositorySnapshotEntry entry, bool openInEditor)
    {
        var repository = _files.CurrentRepository;
        if (repository is null || !_files.SnapshotMatchesSelection) return;

        using var admission = _viewModel.TryBeginUserGitOperation(repository);
        if (admission is null) return;
        try
        {
            var version = await _files.ResolveFileVersionWithinAcceptedOperationAsync(entry);
            if (version is null) return;
            if (!version.CanOpen)
                throw new NotSupportedException(version.UnavailableReason ?? "This historical entry cannot be opened.");

            if (!openInEditor)
            {
                await _fileActions.OpenResolvedVersionWithinAcceptedOperationAsync(
                    repository, version, DiffFileSide.Changed);
                return;
            }

            var materialized = await _fileVersionService.MaterializeAsync(
                repository, version, DiffFileSide.Changed);
            await _externalGitToolService.OpenEditorAsync(repository, materialized.Path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException exception)
            when (openInEditor && exception.Message.Contains("editor", StringComparison.OrdinalIgnoreCase))
        {
            await ShowGitEditorUnavailableAsync(exception.Message);
        }
        catch (Exception exception)
        {
            await _showError(
                openInEditor ? "Could not open historical file in editor" : "Could not open historical file",
                FileActionsViewModel.UserFacingFileError(exception));
        }
    }

    private async Task ShowGitEditorUnavailableAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Git editor is not configured",
            Content = message,
            PrimaryButtonText = "Open Git Tools Settings",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await _showDialog(dialog) == ContentDialogResult.Primary)
            _settings.Open(SettingsSection.GitTools);
    }

    private void AddMenuItem(MenuFlyout flyout, string text, Func<bool> canExecute, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = canExecute() };
        void UpdateAvailability(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(OpenRepositoryViewModel.IsBusy)
                or nameof(OpenRepositoryViewModel.Repository)
                or nameof(OpenRepositoryViewModel.CanChangeRepository))
                item.IsEnabled = canExecute();
        }

        void Detach(object? sender, object args)
        {
            _viewModel.PropertyChanged -= UpdateAvailability;
            flyout.Closed -= Detach;
        }

        _viewModel.PropertyChanged += UpdateAvailability;
        flyout.Closed += Detach;
        item.Click += async (_, _) => await action();
        flyout.Items.Add(item);
    }
}
