using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace CSharpGit.Presentation.Workflows;

/// <summary>Owns workspace-level keyboard routing and its event subscription.</summary>
internal sealed class WorkspaceKeyboardController(
    UIElement root, Func<bool> isShuttingDown, Func<bool> isModalVisible,
    Func<bool> isWorkingTreeVisible, Func<Task> openRepository,
    Func<Task> showHistory, Action focusHistoryFilter, Func<Task> refresh,
    Func<Task> commit, Action<Func<Task>, string> runOperation)
{
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        root.KeyDown += OnKeyDown;
    }

    public void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        root.KeyDown -= OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (isShuttingDown() || isModalVisible()) return;
        var control = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        if (control && e.Key == VirtualKey.O)
        {
            e.Handled = true;
            runOperation(openRepository, "Open repository shortcut");
        }
        else if (control && e.Key == VirtualKey.F)
        {
            e.Handled = true;
            runOperation(async () =>
            {
                if (isWorkingTreeVisible()) await showHistory();
                if (!isShuttingDown()) focusHistoryFilter();
            }, "History search shortcut");
        }
        else if (e.Key == VirtualKey.F5)
        {
            e.Handled = true;
            runOperation(refresh, "Refresh shortcut");
        }
        else if (control && e.Key == VirtualKey.Enter && isWorkingTreeVisible())
        {
            e.Handled = true;
            runOperation(commit, "Commit shortcut");
        }
        else if (e.Key == VirtualKey.Escape && isWorkingTreeVisible())
        {
            e.Handled = true;
            runOperation(showHistory, "Back to history shortcut");
        }
    }
}
