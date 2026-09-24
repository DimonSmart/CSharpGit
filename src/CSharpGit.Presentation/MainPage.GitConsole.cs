using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const double DefaultGitConsoleHeight = 280;
    private const VirtualKey GitConsoleShortcutKey = (VirtualKey)0xC0; // OEM grave/backtick key.
    private IGitCommandActivitySource _gitCommandActivitySource = null!;
    private IAppSettingsService _gitConsoleSettings = null!;
    private GitConsoleView? _gitConsoleView;
    private Controls.GridSplitter? _gitConsoleSplitter;
    private Button? _gitCommandStatusButton;
    private TextBlock? _gitCommandStatusText;
    private RowDefinition? _gitConsoleSplitterRow;
    private RowDefinition? _gitConsoleRow;
    private double _lastGitConsoleHeight = DefaultGitConsoleHeight;
    private bool _gitConsoleInitialized;
    private volatile bool _gitConsoleOpen;

    private void InitializeGitConsole()
    {
        if (_gitConsoleInitialized) return;
        _gitConsoleInitialized = true;

        BuildGitConsoleLayout();
        _gitConsoleView?.SetActivityResolver(_gitCommandActivitySource.Get);
        _gitCommandActivitySource.Changed += GitCommandActivitySource_Changed;
        RootLayout.KeyDown += GitConsole_KeyDown;
        UpdateGitCommandStatus();
    }

    private void BuildGitConsoleLayout()
    {
        while (RepositoryWorkspace.RowDefinitions.Count < 6)
            RepositoryWorkspace.RowDefinitions.Add(new RowDefinition());

        Grid.SetRow(StatusBar, 5);
        _gitConsoleSplitterRow = RepositoryWorkspace.RowDefinitions[3];
        _gitConsoleRow = RepositoryWorkspace.RowDefinitions[4];
        RepositoryWorkspace.RowDefinitions[5].Height = GridLength.Auto;
        _gitConsoleSplitterRow.Height = new GridLength(0);
        _gitConsoleRow.Height = new GridLength(0);
        _gitConsoleRow.MinHeight = 0;
        _gitConsoleRow.MaxHeight = 600;

        _gitConsoleSplitter = new Controls.GridSplitter
        {
            Name = "GitConsoleHeightSplitter",
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            MinimumFirst = 220,
            MinimumSecond = 140,
            MaximumSecond = 600,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(_gitConsoleSplitter, 3);
        RepositoryWorkspace.Children.Add(_gitConsoleSplitter);

        _gitConsoleView = new GitConsoleView { Visibility = Visibility.Collapsed };
        _gitConsoleView.CloseRequested += (_, _) => CloseGitConsole();
        _gitConsoleView.FilterChanged += (_, _) => RebuildGitConsole();
        Grid.SetRow(_gitConsoleView, 4);
        RepositoryWorkspace.Children.Add(_gitConsoleView);

        if (StatusBar.Child is Grid statusGrid)
        {
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
            _gitCommandStatusText = new TextBlock
            {
                Text = "No Git commands",
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 410,
                Opacity = 0.82
            };
            _gitCommandStatusButton = new Button
            {
                Content = _gitCommandStatusText,
                Padding = new Thickness(6, 1, 6, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = null,
                BorderThickness = new Thickness(0)
            };
            _gitCommandStatusButton.Click += (_, _) => ToggleGitConsoleFromStatus();
            Grid.SetColumn(_gitCommandStatusButton, 5);
            statusGrid.Children.Add(_gitCommandStatusButton);
        }
    }

    private void GitCommandActivitySource_Changed(object? sender, GitCommandActivityChangedEventArgs e)
    {
        if (IsShuttingDown || !_gitConsoleInitialized) return;
        if (e.ChangeKind == GitCommandActivityChangeKind.Output)
        {
            if (!_gitConsoleOpen || _gitConsoleView is null || e.OutputStream is not { } stream)
                return;

            _gitConsoleView.QueueOutput(
                e.ActivityId,
                stream,
                e.OutputChunk ?? string.Empty,
                e.OutputRequiresResync);
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyGitCommandLifecycleChange(e);
            return;
        }

        DispatcherQueue.TryEnqueue(() => ApplyGitCommandLifecycleChange(e));
    }

    private void ApplyGitCommandLifecycleChange(GitCommandActivityChangedEventArgs e)
    {
        if (IsShuttingDown || !_gitConsoleInitialized || e.Activity is not { } activity) return;

        if (_gitConsoleOpen && _gitConsoleView is not null)
        {
            if (e.ChangeKind == GitCommandActivityChangeKind.Started)
                _gitConsoleView.ApplyStarted(activity, e.EvictedActivityId);
            else
                _gitConsoleView.ApplyLifecycle(activity, e.EvictedActivityId);
        }

        if (ShouldUpdateGitCommandStatus(e))
            UpdateGitCommandStatus();

        if (activity.CommandKind != GitCommandKind.User || _gitConsoleSettings is null) return;
        var shouldOpen =
            (_gitConsoleSettings.GitConsoleAutoOpenMode == GitConsoleAutoOpenMode.Always &&
             e.ChangeKind == GitCommandActivityChangeKind.Started) ||
            (_gitConsoleSettings.GitConsoleAutoOpenMode == GitConsoleAutoOpenMode.OnErrors &&
             activity.Status == GitCommandStatus.Failed);
        if (shouldOpen)
            OpenGitConsole(activity.Id, manualOpen: false);
    }

    private bool ShouldUpdateGitCommandStatus(GitCommandActivityChangedEventArgs e)
    {
        if (_gitCommandActivitySource is null) return false;
        var filter = _gitConsoleView?.Filter ?? GitCommandFilter.UserCommands;
        if (filter == GitCommandFilter.AllCommands) return true;
        if (e.CommandKind == GitCommandKind.User || e.EvictedCommandKind == GitCommandKind.User) return true;
        return _gitCommandActivitySource.GetLatest(GitCommandFilter.UserCommands) is null;
    }

    private void RebuildGitConsole(Guid? preferredSelection = null)
    {
        if (!_gitConsoleOpen || _gitCommandActivitySource is null || _gitConsoleView is null) return;
        _gitConsoleView.SetActivities(_gitCommandActivitySource.GetSnapshot(_gitConsoleView.Filter), preferredSelection);
        UpdateGitCommandStatus();
    }

    private void UpdateGitCommandStatus()
    {
        if (_gitCommandActivitySource is null || _gitCommandStatusText is null || _gitCommandStatusButton is null) return;
        var filter = _gitConsoleView?.Filter ?? GitCommandFilter.UserCommands;
        var activity = _gitCommandActivitySource.GetLatest(filter)
            ?? _gitCommandActivitySource.GetLatest(GitCommandFilter.AllCommands);
        if (activity is null)
        {
            _gitCommandStatusText.Text = "No Git commands";
            ToolTipService.SetToolTip(_gitCommandStatusButton, "No Git commands recorded in this session.");
            return;
        }

        _gitCommandStatusText.Text = StatusSummary(activity);
        ToolTipService.SetToolTip(_gitCommandStatusButton, activity.DisplayCommand);
    }

    private void ToggleGitConsoleFromStatus()
    {
        if (_gitConsoleOpen)
        {
            CloseGitConsole();
            return;
        }

        if (_gitCommandActivitySource is null) return;
        var filter = _gitConsoleView?.Filter ?? GitCommandFilter.UserCommands;
        var activity = _gitCommandActivitySource.GetLatest(filter)
            ?? _gitCommandActivitySource.GetLatest(GitCommandFilter.AllCommands);
        OpenGitConsole(activity?.Id, manualOpen: true);
    }

    private void ToggleGitConsoleFromKeyboard()
    {
        if (_gitConsoleOpen)
        {
            CloseGitConsole();
            return;
        }
        OpenGitConsole(null, manualOpen: true);
    }

    private void OpenGitConsole(Guid? preferredSelection, bool manualOpen)
    {
        if (_gitCommandActivitySource is null || _gitConsoleView is null ||
            _gitConsoleSplitter is null || _gitConsoleSplitterRow is null || _gitConsoleRow is null) return;

        if (_gitConsoleOpen)
        {
            if (preferredSelection is { } alreadyOpenId)
                _gitConsoleView.SelectActivity(alreadyOpenId);
            return;
        }

        if (manualOpen && preferredSelection is null)
        {
            var latestUser = _gitCommandActivitySource.GetLatest(GitCommandFilter.UserCommands);
            var latest = latestUser ?? _gitCommandActivitySource.GetLatest(GitCommandFilter.AllCommands);
            preferredSelection = latest?.Id;
            if (latestUser is null && latest is not null)
                _gitConsoleView.SetFilter(GitCommandFilter.AllCommands);
        }
        else if (preferredSelection is { } selectedId &&
                 _gitCommandActivitySource.Get(selectedId) is { CommandKind: GitCommandKind.Internal } &&
                 _gitConsoleView.Filter == GitCommandFilter.UserCommands)
        {
            _gitConsoleView.SetFilter(GitCommandFilter.AllCommands);
        }

        _gitConsoleOpen = true;
        _gitConsoleSplitterRow.Height = new GridLength(6);
        _gitConsoleRow.MinHeight = 140;
        _gitConsoleRow.Height = new GridLength(Math.Clamp(_lastGitConsoleHeight, 140, 600));
        _gitConsoleSplitter.Visibility = Visibility.Visible;
        _gitConsoleView.Visibility = Visibility.Visible;
        _gitConsoleView.SetActive(true);
        RebuildGitConsole(preferredSelection);
    }

    private void CloseGitConsole()
    {
        if (!_gitConsoleOpen || _gitConsoleView is null || _gitConsoleSplitter is null ||
            _gitConsoleSplitterRow is null || _gitConsoleRow is null) return;

        if (_gitConsoleRow.ActualHeight >= 140)
            _lastGitConsoleHeight = _gitConsoleRow.ActualHeight;
        _gitConsoleOpen = false;
        _gitConsoleView.SetActive(false);
        _gitConsoleView.Visibility = Visibility.Collapsed;
        _gitConsoleSplitter.Visibility = Visibility.Collapsed;
        _gitConsoleSplitterRow.Height = new GridLength(0);
        _gitConsoleRow.MinHeight = 0;
        _gitConsoleRow.Height = new GridLength(0);
    }

    private void GitConsole_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != GitConsoleShortcutKey || !IsGitConsoleModifierDown()) return;
        ToggleGitConsoleFromKeyboard();
        e.Handled = true;
    }

    private static bool IsGitConsoleModifierDown()
    {
        var control = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        if (control) return true;
        if (!OperatingSystem.IsMacOS()) return false;
        var leftCommand = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        var rightCommand = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        return leftCommand || rightCommand;
    }

    private void ShutdownGitConsole()
    {
        if (!_gitConsoleInitialized) return;
        _gitConsoleInitialized = false;
        _gitCommandActivitySource.Changed -= GitCommandActivitySource_Changed;
        RootLayout.KeyDown -= GitConsole_KeyDown;
        _gitConsoleOpen = false;
        _gitConsoleView?.SetActive(false);
    }

    private static string StatusSummary(GitCommandActivity activity) => activity.Status switch
    {
        GitCommandStatus.Running => $"◌ {activity.DisplayCommand}…",
        GitCommandStatus.Succeeded => $"✓ {activity.DisplayCommand} · {GitConsoleView.FormatDuration(activity.Duration)}",
        GitCommandStatus.Failed => $"✕ {activity.DisplayCommand} · exit {activity.ExitCode}",
        GitCommandStatus.Cancelled => $"○ {activity.DisplayCommand} · cancelled",
        _ => activity.DisplayCommand
    };
}
