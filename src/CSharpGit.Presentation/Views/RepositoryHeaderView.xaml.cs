using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Views;

public sealed partial class RepositoryHeaderView : UserControl
{
    public RepositoryHeaderView() => InitializeComponent();

    internal void Configure(OpenRepositoryViewModel repository, IAppSettingsService settings,
        IDesktopShellService shell, RepositorySelectorActions actions) =>
        Selector.Configure(repository, settings, shell, actions);

    internal void Refresh() => Selector.Refresh();

    internal void Shutdown() => Selector.Shutdown();
}
