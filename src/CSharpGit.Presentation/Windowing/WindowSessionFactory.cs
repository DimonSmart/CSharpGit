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
            return (page, new WindowSession(scope, page, page.WindowRepository));
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
