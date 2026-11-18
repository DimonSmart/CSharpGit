using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    // Phase-3 progress modal remains outside the console host.
    // This bridge is deleted when GitCommandFeedback moves to its own controller.
    private IGitCommandActivitySource _gitCommandActivitySource = null!;
    private IAppSettingsService _gitConsoleSettings = null!;
    private bool _gitConsoleOpen => GitConsoleHost.IsOpen;

    private void InitializeGitConsole()
    {
        InitializeGitCommandFeedback();
        GitConsoleHost.Configure(
            _gitCommandActivitySource,
            _gitConsoleSettings,
            RootLayout,
            RouteGitCommandFeedback,
            RenderGitCommandFeedback,
            () => GitCommandFeedbackModalHost.Visibility == Visibility.Visible);
    }

    private void OpenGitConsole(Guid? preferredSelection, bool manualOpen) =>
        GitConsoleHost.Open(preferredSelection, manualOpen);

    private void CloseGitConsole() => GitConsoleHost.Close();

    private void ShutdownGitConsole()
    {
        GitConsoleHost.Shutdown();
        ShutdownGitCommandFeedback();
    }
}
