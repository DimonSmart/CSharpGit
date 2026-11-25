using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Windowing;

/// <summary>Owns live availability subscriptions for dynamically built repository menus.</summary>
internal sealed class RepositoryMenuItemFactory(OpenRepositoryViewModel repository, WindowUiOperations ui)
{
    private readonly HashSet<Action> _detach = [];
    private bool _shutdown;

    internal void AddMenuItem(MenuFlyout flyout, string text, bool enabled, Func<Task> action) =>
        AddMenuItem(flyout, text, () => enabled, action);

    internal void AddMenuItem(MenuFlyout flyout, string text, Func<bool> canExecute, Func<Task> action)
    {
        if (_shutdown) return;
        var item = new MenuFlyoutItem { Text = text, IsEnabled = canExecute() };

        void UpdateAvailability(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(OpenRepositoryViewModel.IsBusy)
                or nameof(OpenRepositoryViewModel.Repository)
                or nameof(OpenRepositoryViewModel.CanChangeRepository))
                item.IsEnabled = canExecute();
        }

        void OnClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) =>
            ui.RunPageUiOperation(action, "Menu action", showError: true);

        void Detach()
        {
            repository.PropertyChanged -= UpdateAvailability;
            flyout.Closed -= OnClosed;
            item.Click -= OnClick;
            _detach.Remove(Detach);
        }

        void OnClosed(object? sender, object args) => Detach();

        _detach.Add(Detach);
        repository.PropertyChanged += UpdateAvailability;
        flyout.Closed += OnClosed;
        item.Click += OnClick;
        flyout.Items.Add(item);
    }

    internal void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        foreach (var detach in _detach.ToArray()) detach();
    }
}
