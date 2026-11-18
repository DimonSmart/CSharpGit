using CSharpGit.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpGit.Presentation.Windowing;

internal sealed class WindowSession(
    IServiceScope scope,
    IWindowContentLifetime content,
    OpenRepositoryViewModel repository,
    WindowCloseSafetyProvider closeSafety,
    Func<CancellationToken, Task> initializeContent,
    WindowTitleState titleState)
{
    private readonly WindowSessionLifecycle _lifetime = new();

    internal OpenRepositoryViewModel Repository { get; } = repository;
    internal string CurrentWindowTitle => titleState.CurrentTitle;
    internal event Action<string>? WindowTitleChanged
    {
        add => titleState.TitleChanged += value;
        remove => titleState.TitleChanged -= value;
    }
    internal bool IsUnsafeToClose => closeSafety.IsUnsafeToClose;
    internal bool RequiresCloseConfirmation => closeSafety.RequiresCloseConfirmation;
    internal WindowSessionState State => _lifetime.State;
    internal bool IsActive => State == WindowSessionState.Active;

    internal Task InitializeAsync(CancellationToken cancellationToken) =>
        _lifetime.InitializeAsync(initializeContent, CompleteShutdown, cancellationToken);

    internal Task PrepareForShutdownAsync(CancellationToken cancellationToken) =>
        _lifetime.PrepareAsync(content.PrepareForShutdownAsync, cancellationToken);

    internal void CompleteShutdown()
    {
        if (!_lifetime.TryComplete()) return;
        try { content.CompleteShutdown(); }
        finally
        {
            try { titleState.Dispose(); }
            finally { scope.Dispose(); }
        }
    }
}
