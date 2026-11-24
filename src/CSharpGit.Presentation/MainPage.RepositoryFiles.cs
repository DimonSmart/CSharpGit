using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly RepositoryFilesViewModel _repositoryFilesViewModel = null!;
    private PivotItem? _repositoryFilesTab;
    private RepositoryFilesSurface? _repositoryFilesSurface;
    private readonly Workflows.RepositoryFilePreviewController _repositoryFilePreview = null!;
    private Workflows.RepositoryFilesActionController _repositoryFilesActions = null!;

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
        _repositoryFilesTab = new PivotItem { Header = "Files", Name = "FilesTab" };
        _repositoryFilesSurface = new RepositoryFilesSurface(_repositoryFilesViewModel);
        _repositoryFilesTab.Content = _repositoryFilesSurface;
        DetailsTabs.Items.Add(_repositoryFilesTab);
    }

    private void ConfigureRepositoryFilesActions()
    {
        _repositoryFilesActions = new Workflows.RepositoryFilesActionController(
            _viewModel, _repositoryFilesViewModel, _fileVersionService,
            _externalGitToolService, _fileActions, _historyRewrite,
            _settingsWindowController, () => XamlRoot, ShowPageDialogAsync,
            ShowErrorAsync, CopyTextAsync);

        _repositoryFilesSurface!.Configure(
            _repositoryFilesViewModel, _repositoryFilePreview,
            () => ReferenceEquals(DetailsTabs.SelectedItem, _repositoryFilesTab),
            () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description),
            entry => _submodules.OpenSelectedSubmoduleAsync(entry),
            (target, entry) => _submodules.ShowSubmoduleMenuAsync(target, entry),
            _repositoryFilesActions.OpenAsync,
            _repositoryFilesActions.ShowMenu);
    }

    private void UpdateRepositoryFilesViewActivity() => _repositoryFilesSurface?.UpdateActivity();
    private void UpdateRepositoryFilesModeSurface() => _repositoryFilesSurface?.UpdateModeSurface();
}
