using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly RepositoryFilesViewModel _repositoryFilesViewModel = null!;
    private PivotItem? _repositoryFilesTab;
    private PivotItem? _changesTab;
    private RepositoryFilesSurface? _repositoryFilesSurface;
    private readonly Workflows.RepositoryFilePreviewController _repositoryFilePreview = null!;

    private MainPage(
        OpenRepositoryViewModel viewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        IRepositoryFileVersionService fileVersionService,
        IExternalGitToolService externalGitToolService,
        FileActionsViewModel fileActions,
        RepositoryFilesViewModel repositoryFilesViewModel,
        WorkspaceNavigator workspaceNavigator)
        : this(
            viewModel,
            workingTreeStatusReader,
            fileVersionService,
            externalGitToolService,
            fileActions,
            workspaceNavigator)
    {
        _repositoryFilesViewModel = repositoryFilesViewModel
            ?? throw new ArgumentNullException(nameof(repositoryFilesViewModel));
        _repositoryFilesViewModel.Attach(_viewModel.CommitDetails);
        _repositoryFilePreview = new Workflows.RepositoryFilePreviewController(
            _viewModel, _repositoryFilesViewModel, _fileVersionService,
            () => _repositoryFilesSurface?.PreviewHost,
            () => IsShuttingDown,
            () => _repositoryFilesSurface?.IsRepositoryFilesActive == true,
            () => _repositoryFilesSurface?.ActivationRevision ?? 0,
            (operation, description) => RunPageUiOperation(operation, description),
            ReportUiFailure,
            UserFacingFileError);
        InitializeRepositoryFiles();
    }

    private void InitializeRepositoryFiles()
    {
        _changesTab = ChangesTab;
        _changesTab.Name = "ChangesTab";
        _repositoryFilesTab = new PivotItem { Header = "Files", Name = "FilesTab" };
        _repositoryFilesSurface = new RepositoryFilesSurface(_repositoryFilesViewModel);
        _repositoryFilesTab.Content = _repositoryFilesSurface;
        DetailsTabs.Items.Add(_repositoryFilesTab);
        CommitFilesList.DoubleTapped -= CommitFilesList_DoubleTapped;
        CommitFilesList.KeyDown -= CommitFilesList_KeyDown;
        CommitFilesList.DoubleTapped += RepositoryChangedFiles_DoubleTapped;
        CommitFilesList.KeyDown += RepositoryChangedFiles_KeyDown;
        _repositoryFilesSurface.Configure(
            _repositoryFilesViewModel, _repositoryFilePreview,
            () => ReferenceEquals(DetailsTabs.SelectedItem, _repositoryFilesTab),
            () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description),
            entry => _submodules.OpenSelectedSubmoduleAsync(entry),
            (target, entry) => _submodules.ShowSubmoduleMenuAsync(target, entry),
            OpenRepositorySnapshotFileAsync, ShowRepositorySnapshotFileMenu);
    }

    private void UpdateRepositoryFilesViewActivity() => _repositoryFilesSurface?.UpdateActivity();
    private void UpdateRepositoryFilesModeSurface() => _repositoryFilesSurface?.UpdateModeSurface();

    private void ShowRepositorySnapshotFileMenu(
        FrameworkElement target,
        RepositorySnapshotEntry entry)
    {
        var flyout = new MenuFlyout();
        if (entry.Kind == RepositorySnapshotEntryKind.File)
        {
            AddMenuItem(
                flyout,
                "Open",
                () => _viewModel.CanStartUserGitOperation,
                () => OpenRepositorySnapshotFileAsync(entry, openInEditor: false));
            AddMenuItem(
                flyout,
                "Open in editor",
                () => _viewModel.CanStartUserGitOperation,
                () => OpenRepositorySnapshotFileAsync(entry, openInEditor: true));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Remove from repository history…",
                () => !_viewModel.IsBusy && !_viewModel.RepositoryHistoryRewrite.IsInProgress,
                () => _historyRewrite.RemovePathAsync(entry));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddMenuItem(flyout, "Copy path", true, () => CopyTextAsync(entry.Path));
        flyout.ShowAt(target);
    }

    private async Task OpenRepositorySnapshotFileAsync(
        RepositorySnapshotEntry entry,
        bool openInEditor)
    {
        var repository = _repositoryFilesViewModel.CurrentRepository;
        if (repository is null || !_repositoryFilesViewModel.SnapshotMatchesSelection)
            return;

        using var admission = _viewModel.TryBeginUserGitOperation(repository);
        if (admission is null) return;
        try
        {
            var version = await _repositoryFilesViewModel.ResolveFileVersionWithinAcceptedOperationAsync(entry);
            if (version is null) return;
            if (!version.CanOpen)
            {
                throw new NotSupportedException(
                    version.UnavailableReason ?? "This historical entry cannot be opened.");
            }

            if (!openInEditor)
            {
                await _fileActions.OpenResolvedVersionWithinAcceptedOperationAsync(repository, version, DiffFileSide.Changed);
                return;
            }

            var materialized = await _fileVersionService.MaterializeAsync(
                repository,
                version,
                DiffFileSide.Changed);
            await _externalGitToolService.OpenEditorAsync(repository, materialized.Path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException exception)
            when (openInEditor
                  && exception.Message.Contains("editor", StringComparison.OrdinalIgnoreCase))
        {
            await ShowGitEditorUnavailableAsync(exception.Message);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(
                openInEditor
                    ? "Could not open historical file in editor"
                    : "Could not open historical file",
                UserFacingFileError(exception));
        }
    }

    private async Task ShowGitEditorUnavailableAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Git editor is not configured",
            Content = message,
            PrimaryButtonText = "Open Git Tools Settings",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            _settingsWindowController.Open(SettingsSection.GitTools);
    }

    private void RepositoryChangedFiles_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (CommitFilesList.SelectedItem is null || _changesTab is null) return;
        DetailsTabs.SelectedItem = _changesTab;
        args.Handled = true;
    }

    private void RepositoryChangedFiles_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || CommitFilesList.SelectedItem is null || _changesTab is null)
            return;

        DetailsTabs.SelectedItem = _changesTab;
        args.Handled = true;
    }

}
