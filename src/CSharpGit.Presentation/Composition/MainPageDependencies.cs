using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation;

// Typed feature groups keep MainPage composition explicit without a service-locator constructor.
internal sealed record MainPageRepositoryDependencies(
    OpenRepositoryViewModel ViewModel,
    CreateRepositoryViewModel CreateRepository,
    CloneRepositoryViewModel CloneRepository,
    RepositoryFilesViewModel RepositoryFiles,
    FileActionsViewModel FileActions,
    RepositoryMaintenanceViewModel Maintenance,
    WorktreesViewModel Worktrees,
    WorkspaceNavigator WorkspaceNavigator);

internal sealed record MainPageGitDependencies(
    IWorkingTreeStatusReader WorkingTreeStatusReader,
    IDesktopShellService DesktopShell,
    IRepositoryFileVersionService FileVersionService,
    IExternalGitToolService ExternalGitToolService,
    ISubmoduleService SubmoduleService);

internal sealed record MainPageUiDependencies(
    IAppSettingsService AppSettings,
    IGitCommandActivitySource GitCommandActivitySource,
    IRepositoryImageService RepositoryImageService,
    IAuthorAvatarService AuthorAvatarService,
    RecentRepositoryFolderPicker RecentRepositoryFolderPicker,
    SettingsWindowController SettingsWindowController);

internal sealed record MainPageUpdateDependencies(
    IApplicationVersionProvider ApplicationVersionProvider,
    IUpdateCheckService UpdateCheckService,
    IApplicationUpdateInstaller ApplicationUpdateInstaller,
    ISystemUriLauncher SystemUriLauncher,
    ApplicationUpdateActivityState UpdateActivityState);
