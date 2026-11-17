using Microsoft.Extensions.DependencyInjection;

namespace CSharpGit.Presentation.Windowing;

// The factory alone knows the legacy visual root; WindowSession does not.
internal sealed class WindowSessionFactory(IServiceScopeFactory scopeFactory)
{
    internal (MainPage Page, WindowSession Session) Create()
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            var page = scope.ServiceProvider.GetRequiredService<MainPage>();
            var safety = new WindowCloseSafetyProvider(
                page.WindowRepository,
                page.DesktopCloneForCheck,
                page.DesktopCreateForCheck,
                page.DesktopMaintenanceForCheck,
                () => page.IsApplicationUpdateInProgress);
            return (page, new WindowSession(
                scope,
                page,
                page.WindowRepository,
                safety,
                _ => Program.InitialRepositoryPath is not null
                    ? page.OpenInitialRepositoryAsync()
                    : Task.CompletedTask));
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
