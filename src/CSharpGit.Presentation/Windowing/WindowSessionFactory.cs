using Microsoft.Extensions.DependencyInjection;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Windowing;

// The factory alone knows the legacy visual root; WindowSession does not.
internal sealed class WindowSessionFactory(IServiceScopeFactory scopeFactory)
{
    internal (MainPage Page, WindowSession Session) Create()
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
            var page = scope.ServiceProvider.GetRequiredService<MainPage>();
            page.AttachNavigator(navigator);
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
                page,
                repository,
                navigator,
                safety,
                _ => Program.InitialRepositoryPath is not null
                    ? page.OpenInitialRepositoryAsync()
                    : Task.CompletedTask,
                titleState));
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
