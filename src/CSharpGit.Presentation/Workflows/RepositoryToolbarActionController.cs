using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Dialogs;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;

namespace CSharpGit.Presentation.Workflows;

// Typed toolbar dispatcher. It owns no Git semantics; each action delegates to its feature owner.
internal sealed class RepositoryToolbarActionController(
    OpenRepositoryViewModel viewModel,
    WorkspaceNavigationController navigation,
    RepositoryPullController pull,
    RepositoryPushController push,
    RepositoryManualRefreshController refresh,
    RecentRepositoryPickerWorkflow repositoryPicker,
    RepositoryCloneWorkflow clone,
    RepositoryCreationWorkflow creation,
    RepositorySwitchController repositorySwitch,
    SettingsWindowController settings,
    RepositoryMaintenanceController maintenance,
    AboutUpdateDialogController about,
    Func<System.Windows.Input.ICommand, Task> executeCommand,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly WorkspaceNavigationController _navigation = navigation;
    private readonly RepositoryPullController _pull = pull;
    private readonly RepositoryPushController _push = push;
    private readonly RepositoryManualRefreshController _refresh = refresh;
    private readonly RecentRepositoryPickerWorkflow _repositoryPicker = repositoryPicker;
    private readonly RepositoryCloneWorkflow _clone = clone;
    private readonly RepositoryCreationWorkflow _creation = creation;
    private readonly RepositorySwitchController _repositorySwitch = repositorySwitch;
    private readonly SettingsWindowController _settings = settings;
    private readonly RepositoryMaintenanceController _maintenance = maintenance;
    private readonly AboutUpdateDialogController _about = about;
    private readonly Func<System.Windows.Input.ICommand, Task> _executeCommand = executeCommand;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;

    internal void Handle(RepositoryToolbarAction action)
    {
        switch (action)
        {
            case RepositoryToolbarAction.CommitNavigation:
                _runUiOperation(_navigation.ToggleCommitNavigationAsync, "Toggle history / working tree");
                break;
            case RepositoryToolbarAction.Fetch:
                _runUiOperation(() => _executeCommand(_viewModel.RepositorySync.FetchCommand), "Fetch");
                break;
            case RepositoryToolbarAction.Pull:
                _runUiOperation(() => _pull.PullAsync(null), "Pull");
                break;
            case RepositoryToolbarAction.PullFastForwardOnly:
                _runUiOperation(() => _pull.PullAsync(PullStrategy.FastForwardOnly), "Fast-forward pull");
                break;
            case RepositoryToolbarAction.PullMerge:
                _runUiOperation(() => _pull.PullAsync(PullStrategy.Merge), "Merge pull");
                break;
            case RepositoryToolbarAction.PullRebase:
                _runUiOperation(() => _pull.PullAsync(PullStrategy.Rebase), "Rebase pull");
                break;
            case RepositoryToolbarAction.PullSettings:
                _settings.Open(SettingsSection.General, focusPullStrategy: true);
                break;
            case RepositoryToolbarAction.PushPrimary:
                _runUiOperation(_push.PushPrimaryAsync, "Push");
                break;
            case RepositoryToolbarAction.Push:
                _runUiOperation(_push.PushAsync, "Push");
                break;
            case RepositoryToolbarAction.PushTo:
                _runUiOperation(_push.PushToAsync, "PushTo_Click");
                break;
            case RepositoryToolbarAction.ForcePushWithLease:
                _runUiOperation(_push.ForcePushWithLeaseAsync, "ForcePushWithLease_Click");
                break;
            case RepositoryToolbarAction.PushSetDefault:
                _runUiOperation(
                    () => _viewModel.RepositorySync.SetDefaultPushActionAsync(PushToolbarAction.Push),
                    "Set default Push");
                break;
            case RepositoryToolbarAction.PushToSetDefault:
                _runUiOperation(
                    () => _viewModel.RepositorySync.SetDefaultPushActionAsync(PushToolbarAction.PushTo),
                    "Set default Push to");
                break;
            case RepositoryToolbarAction.ForcePushWithLeaseSetDefault:
                _runUiOperation(
                    () => _viewModel.RepositorySync.SetDefaultPushActionAsync(PushToolbarAction.ForcePushWithLease),
                    "Set default Force push with lease");
                break;
            case RepositoryToolbarAction.RefreshIndicator:
                _runUiOperation(_refresh.RefreshAsync, "Repository refresh indicator");
                break;
            case RepositoryToolbarAction.OpenRepository:
                _runUiOperation(_repositoryPicker.OpenAsync, "OpenRepository_Click");
                break;
            case RepositoryToolbarAction.CloneRepository:
                _runUiOperation(_clone.ShowAsync, "CloneRepository_Click");
                break;
            case RepositoryToolbarAction.CreateRepository:
                _runUiOperation(_creation.ShowAsync, "CreateRepository_Click");
                break;
            case RepositoryToolbarAction.Repositories:
            case RepositoryToolbarAction.CloseRepository:
                _runUiOperation(async () => { await _repositorySwitch.TryCloseAsync(); }, "CloseRepository_Click");
                break;
            case RepositoryToolbarAction.RepositorySettings:
                _settings.Open(SettingsSection.Identity);
                break;
            case RepositoryToolbarAction.OptimizeRepository:
                _runUiOperation(_maintenance.OptimizeAsync, "Optimize repository");
                break;
            case RepositoryToolbarAction.Settings:
                _settings.Open(SettingsSection.General);
                break;
            case RepositoryToolbarAction.About:
                _runUiOperation(_about.ShowAboutAsync, "About dialog");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }
}
