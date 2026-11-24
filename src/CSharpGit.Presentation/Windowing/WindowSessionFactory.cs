using Microsoft.Extensions.DependencyInjection;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Windowing;

// The factory alone knows the legacy visual root; WindowSession does not.
internal sealed class WindowSessionFactory(IServiceScopeFactory scopeFactory)
{
    internal (MainPage Page, WindowSession Session, ICloseConfirmationDialog Confirmation,
        Action QueueCommitDetailsLayout, Diagnostics.DesktopLifecycleCheckContext DesktopLifecycleCheck) Create()
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            // The scope, not the repository ViewModel, owns DI-created feature models.
            var repository = scope.ServiceProvider.GetRequiredService<OpenRepositoryViewModel>();
            repository.UseScopeOwnedChildren();
            var clone = scope.ServiceProvider.GetRequiredService<CloneRepositoryViewModel>();
            var create = scope.ServiceProvider.GetRequiredService<CreateRepositoryViewModel>();
            var maintenance = scope.ServiceProvider.GetRequiredService<RepositoryMaintenanceViewModel>();
            var updateActivity = scope.ServiceProvider.GetRequiredService<ApplicationUpdateActivityState>();
            var navigator = scope.ServiceProvider.GetRequiredService<WorkspaceNavigator>();
            var composition = scope.ServiceProvider.GetRequiredService<WindowWorkspaceComposition>();
            var page = composition.Page;
            var confirmation = new CommitDraftCloseConfirmationDialog(
                repository, composition.ShowCommitDraftDialogAsync, () => composition.IsClosingForDialog);
            composition.ConfigureCloseConfirmation(confirmation);
            var safety = new WindowCloseSafetyProvider(
                repository, clone, create, maintenance, updateActivity);
            var titleState = new WindowTitleState(
                repository,
                () => MainWindowTitleFormatter.Format(
                    repository.Repository,
                    repository.CurrentBranchName,
                    repository.CurrentHeadCommit,
                    repository.IsDetachedHead),
                nameof(OpenRepositoryViewModel.Repository),
                nameof(OpenRepositoryViewModel.CurrentBranchName),
                nameof(OpenRepositoryViewModel.CurrentHeadCommit),
                nameof(OpenRepositoryViewModel.IsDetachedHead));
            return (page, new WindowSession(
                scope,
                _ => composition.PrepareForShutdown(),
                composition.BeginShutdown,
                repository,
                navigator,
                safety,
                _ => Program.InitialRepositoryPath is not null
                    ? composition.OpenInitialRepositoryAsync()
                    : Task.CompletedTask,
                titleState), confirmation, composition.QueueCommitDetailsLayout, composition.CreateDesktopLifecycleCheckContext());
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
