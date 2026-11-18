using System.Text.Json;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Diagnostics;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private sealed partial class DesktopUiCheckRunner(MainPage page)
    {
        private readonly MainPage _page = page;

        private sealed class XamlOnlyProbeData : System.ComponentModel.INotifyPropertyChanged
        {
            private string _probeText = "XAML-only binding verified";
            public string ProbeText
            {
                get => _probeText;
                set
                {
                    if (_probeText == value) return;
                    _probeText = value;
                    PropertyChanged?.Invoke(this,
                        new System.ComponentModel.PropertyChangedEventArgs(nameof(ProbeText)));
                }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }

        public async Task RunAsync(string resultPath)
        {
            var failures = new List<string>();
            try
            {
                var probeData = new XamlOnlyProbeData();
                var probe = new Views.XamlOnlyProbe { DataContext = probeData };
                _page.RootLayout.Children.Add(probe);
                try
                {
                    var text = (probe.Content as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
                    await WaitUntilAsync(
                        () => text?.Text == "XAML-only binding verified",
                        TimeSpan.FromSeconds(5));
                    Check(text?.Text == "XAML-only binding verified",
                        "generated XAML-only UserControl did not bind its DataContext", failures);
                    probeData.ProbeText = "XAML-only binding updated";
                    await WaitUntilAsync(
                        () => text?.Text == "XAML-only binding updated",
                        TimeSpan.FromSeconds(5));
                    Check(text?.Text == "XAML-only binding updated",
                        "generated XAML-only UserControl did not react to INotifyPropertyChanged", failures);
                }
                finally
                {
                    _page.RootLayout.Children.Remove(probe);
                }

                var busyObserved = false;
                _page._viewModel.PropertyChanged += (_, eventArgs) =>
                {
                    if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy) && _page._viewModel.IsBusy)
                        busyObserved = true;
                };

                await _page._viewModel.OpenRepositoryAsyncForDesktopCheck();
                _page.RefreshPresentationCollections();
                Check(_page._viewModel.Repository is not null, $"repository did not open: {_page._viewModel.ErrorMessage}", failures);
                for (var attempt = 0; attempt < 3 && _page._viewModel.Repository is not null &&
                     (_page._viewModel.WorkingTree.Changes.Count == 0 || _page._viewModel.History.Rows.Count == 0); attempt++)
                {
                    await Task.Delay(100 * (attempt + 1));
                    await _page._viewModel.RefreshAsyncForDesktopCheck();
                    _page.RefreshPresentationCollections();
                }
                Check(_page._viewModel.WorkingTree.Changes.Count > 0, $"working tree changes were not loaded: {_page._viewModel.ErrorMessage}", failures);
                Check(_page._viewModel.History.Rows.Count > 0, $"history was not loaded: {_page._viewModel.ErrorMessage}", failures);
                await WaitUntilAsync(
                    () => _page.RepositoryWorkspace.ActualWidth > 0 && _page.RepositoryWorkspace.ActualHeight > 0 && _page.HistoryList.ActualWidth > 0 && _page.HistoryList.ActualHeight > 0,
                    TimeSpan.FromSeconds(10));
                Check(_page._viewModel.Repository is not null, "repository did not open", failures);
                Check(_page._viewModel.Repository?.IsWorktree == (Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_WORKTREE") == "1"), "repository kind is incorrect", failures);
                Check(_page._viewModel.History.Scopes.All(scope => scope.Label is "All references" or "Current branch"), "English history scopes are missing", failures);

                if (Environment.GetEnvironmentVariable("CSHARPGIT_GRAPH_VIEWPORT_CHECK") == "1")
                {
                    await RunCommitGraphViewportLifecycleCheckAsync(failures);
                    await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(
                        new DesktopCheckResult(failures.Count == 0, failures, false)));
                    return;
                }

                var settings = _page._recentRepositorySettings;
                await settings.SetThemeModeAsync(ApplicationThemeMode.System);
                await WaitUntilAsync(() => _page.RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
                Check(_page.RequestedTheme == ElementTheme.Default, "System theme did not clear the root override", failures);

                await settings.SetThemeModeAsync(ApplicationThemeMode.Light);
                await WaitUntilAsync(() => _page.RequestedTheme == ElementTheme.Light, TimeSpan.FromSeconds(5));
                Check(_page.RequestedTheme == ElementTheme.Light, "Light theme was not applied to the main root", failures);

                _page.OpenSettingsWindow();
                await WaitUntilAsync(() => _page._settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Light }, TimeSpan.FromSeconds(5));
                Check(_page._settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Light }, "new settings root did not receive the current theme", failures);

                var originalCommitTimeMode = settings.CommitTimeDisplayMode;
                var alternateCommitTimeMode = originalCommitTimeMode == CommitTimeDisplayMode.Smart
                    ? CommitTimeDisplayMode.Relative
                    : CommitTimeDisplayMode.Smart;
                await settings.SetCommitTimeDisplayModeAsync(alternateCommitTimeMode);
                Check(_page.RequestedTheme == ElementTheme.Light && _page._settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Light }, "unrelated settings change desynchronized the theme", failures);
                await settings.SetCommitTimeDisplayModeAsync(originalCommitTimeMode);

                await settings.SetThemeModeAsync(ApplicationThemeMode.Dark);
                await WaitUntilAsync(
                    () => _page.RequestedTheme == ElementTheme.Dark && _page._settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Dark },
                    TimeSpan.FromSeconds(5));
                Check(_page.RequestedTheme == ElementTheme.Dark, "Dark theme was not applied to the main root", failures);
                Check(_page._settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Dark }, "Dark theme was not propagated to the settings root", failures);

                _page._settingsWindowController.CloseCurrent();
                await settings.SetThemeModeAsync(ApplicationThemeMode.System);
                await WaitUntilAsync(() => _page.RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
                Check(_page.RequestedTheme == ElementTheme.Default, "Dark to System did not clear the main root override", failures);

                Check(_page.RepositoryWorkspace.ActualWidth > 0 && _page.RepositoryWorkspace.ActualHeight > 0, "workspace was not laid out", failures);
                Check(_page.HistoryList.ActualWidth > 0 && _page.HistoryList.ActualHeight > 0, "history list was not laid out", failures);
                Check(_page.RepositoryTree.ActualWidth > 0 && _page.RepositoryExplorer.RootsForCheck.Count >= 4, "repository tree was not laid out", failures);
                Check(_page.DetailsScroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled && _page.DetailsScroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "detail message wrapping is not configured", failures);
                await WaitUntilAsync(
                    () => _page._commitDetailsView is { ActualWidth: > 0 } && _page.DetailsScroller.ViewportWidth > 0,
                    TimeSpan.FromSeconds(5));
                var commitMessageText = _page._commitDetailsView is null ? null : FindDescendant<TextBlock>(_page._commitDetailsView);
                var historyOrigin = _page.HistoryPane.TransformToVisual(null).TransformPoint(default);
                var availableDetailsWidth = ((Microsoft.UI.Xaml.Application.Current as App)?.MainWindowClientWidth ?? 0) /
                                            (_page.XamlRoot?.RasterizationScale ?? 1) - historyOrigin.X;
                Check(_page._commitDetailsView is not null && _page._commitDetailsView.ActualWidth <= availableDetailsWidth + 1, "commit details exceed the visible window viewport", failures);
                Check(
                    commitMessageText is not null && commitMessageText.ActualHeight > commitMessageText.FontSize * 2,
                    $"long commit message did not wrap to multiple lines: viewport={_page.DetailsScroller.ViewportWidth}, details={_page._commitDetailsView?.ActualWidth}, text={commitMessageText?.ActualWidth}x{commitMessageText?.ActualHeight}, font={commitMessageText?.FontSize}, length={commitMessageText?.Text.Length}, window={(Microsoft.UI.Xaml.Application.Current as App)?.MainWindowClientWidth}, scale={_page.XamlRoot?.RasterizationScale}, historyOrigin={historyOrigin.X}",
                    failures);
                if (Microsoft.UI.Xaml.Application.Current is App resizeCheckApp && commitMessageText is not null)
                {
                    var wideMessageHeight = commitMessageText.ActualHeight;
                    var messageLeft = commitMessageText.TransformToVisual(_page.HistoryPane).TransformPoint(default).X;
                    resizeCheckApp.ResizeMainWindowForCheck(700, 900);
                    await WaitUntilAsync(
                        () => resizeCheckApp.MainWindowClientWidth < 800 &&
                              _page._commitDetailsView!.ActualWidth < availableDetailsWidth - 200 &&
                              commitMessageText.ActualHeight > wideMessageHeight,
                        TimeSpan.FromSeconds(5));
                    var narrowMessageHeight = commitMessageText.ActualHeight;
                    Check(Math.Abs(commitMessageText.TransformToVisual(_page.HistoryPane).TransformPoint(default).X - messageLeft) <= 1,
                        "commit message moved away from its left edge after shrinking the window", failures);
                    resizeCheckApp.ResizeMainWindowForCheck(1400, 900);
                    await WaitUntilAsync(
                        () => _page._commitDetailsView!.ActualWidth >= availableDetailsWidth - 1 &&
                              commitMessageText.ActualHeight < narrowMessageHeight,
                        TimeSpan.FromSeconds(5));
                    Check(Math.Abs(commitMessageText.TransformToVisual(_page.HistoryPane).TransformPoint(default).X - messageLeft) <= 1,
                        "commit message moved away from its left edge after expanding the window", failures);
                }
                Check(CountDescendants<Controls.GridSplitter>(_page.RootLayout) >= 2, "resizable splitters are missing", failures);
                Check(CountDescendants<ScrollViewer>(_page.RootLayout) > 0, "scroll viewers are missing", failures);
                var xamlRoot = _page.XamlRoot;
                Check(xamlRoot is not null && (_page.RootLayout.Clip is not null || _page.RootLayout.ActualWidth <= xamlRoot.Size.Width + 1), "root content exceeds its viewport", failures);
                var splitter = FindDescendant<Controls.GridSplitter>(_page.RootLayout);
                var splitterGrid = splitter?.Parent as Grid;
                var oldWidth = splitterGrid is null ? 0 : splitterGrid.ColumnDefinitions[0].ActualWidth;
                splitter?.ResizeForCheck(24);
                Check(splitterGrid is not null && splitterGrid.ColumnDefinitions[0].Width.IsAbsolute && Math.Abs(splitterGrid.ColumnDefinitions[0].Width.Value - oldWidth) > 1, "splitter did not resize its pane", failures);

                Check(_page.RepositoryToolbar.CommitNavigationLabel == $"Commit ({_page._viewModel.WorkingTree.Changes.Count})", "commit toolbar count is incorrect", failures);
                _page.ShowWorkingTree();
                Check(_page.WorkingTreePane.Visibility == Visibility.Visible && _page.HistoryPane.Visibility == Visibility.Collapsed, "working tree mode did not open", failures);
                Check(_page.RepositoryToolbar.CommitNavigationLabel == "Back to history", "commit toolbar did not become the history navigation action", failures);
                Check(_page._unstagedChanges.Count + _page._stagedChanges.Count >= _page._viewModel.WorkingTree.Changes.Count, "working tree staged/unstaged views lost changes", failures);
                await _page.ShowAllHistoryAsync();
                Check(_page.RepositoryToolbar.CommitNavigationLabel == $"Commit ({_page._viewModel.WorkingTree.Changes.Count})", "commit toolbar did not restore the change count", failures);

                if (_page._viewModel.History.Rows.FirstOrDefault() is { } selectedBeforeRefresh)
                {
                    _page._viewModel.History.SelectedRow = selectedBeforeRefresh;
                    var selectedHash = selectedBeforeRefresh.Commit.Hash;
                    await _page._viewModel.RefreshAsyncForDesktopCheck();
                    Check(_page._viewModel.History.SelectedRow?.Commit.Hash == selectedHash, "history refresh did not preserve the selected commit", failures);
                    Check(_page._viewModel.History.Rows.Any(row => ReferenceEquals(row, _page._viewModel.History.SelectedRow)), "history refresh kept a stale selected row instance", failures);
                }

                if (_page._viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent) is { } currentBranch)
                {
                    await _page.ShowReferenceHistoryAsync(currentBranch.Name, $"Branch: {currentBranch.Name}");
                    Check(_page._viewModel.History.Rows.Any(row => ReferenceEquals(row, _page._viewModel.History.SelectedRow)), "scoped history selection is not part of the current ItemsSource", failures);
                    await _page.ShowAllHistoryAsync();
                    Check(_page._viewModel.History.SelectedRow is null || _page._viewModel.History.Rows.Any(row => ReferenceEquals(row, _page._viewModel.History.SelectedRow)), "all-history selection is not part of the current ItemsSource", failures);
                }

                _page._viewModel.CommitCreation.CommitMessage = "draft retained by close guard";
                Check(_page._viewModel.CommitCreation.HasUnappliedCommitMessage, "commit draft close guard is inactive", failures);
                if (_page._viewModel.WorkingTree.Changes.FirstOrDefault(change => change.IsUnstaged) is { } workingTreeCheckChange)
                    await _page._viewModel.WorkingTree.SelectChangeAsync(workingTreeCheckChange, WorkingTreeDiffKind.Unstaged);
                await WaitUntilAsync(() => !_page._viewModel.IsBusy, TimeSpan.FromSeconds(20));
                Check(_page._viewModel.WorkingTree.StageCommand.CanExecute(null), "Stage must be enabled for an unstaged selection", failures);
                Check(!_page._viewModel.WorkingTree.UnstageCommand.CanExecute(null), "Unstage must be disabled for an unstaged selection", failures);
                var selectedChange = _page._viewModel.WorkingTree.SelectedChange;
                _page._viewModel.WorkingTree.ClearActiveSelection();
                Check(!_page._viewModel.WorkingTree.StageCommand.CanExecute(null) && !_page._viewModel.WorkingTree.UnstageCommand.CanExecute(null), "file commands must be disabled without a selection", failures);
                if (selectedChange is not null)
                    await _page._viewModel.WorkingTree.SelectChangeAsync(selectedChange, WorkingTreeDiffKind.Unstaged);
                await WaitUntilAsync(() => !_page._viewModel.IsBusy, TimeSpan.FromSeconds(20));
                Check(_page._viewModel.CommitCreation.CommitCommand.CanExecute(null), "Commit must be enabled for a non-empty draft", failures);
                _page._viewModel.CommitCreation.CommitCommand.Execute(null);
                await WaitUntilAsync(() => !_page._viewModel.IsBusy, TimeSpan.FromSeconds(20));
                Check(_page._viewModel.CommitCreation.IsEmptyIndexChoiceOpen, "empty index did not present an explicit choice", failures);
                Check(_page._viewModel.CommitCreation.CommitMessage == "draft retained by close guard", "commit draft was lost", failures);
                Check(busyObserved && !_page.RepositoryToolbar.IsRefreshBusy, "busy indication did not transition back to idle", failures);

                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new DesktopCheckResult(failures.Count == 0, failures, _page._viewModel.Repository?.IsWorktree ?? false)));
            }
            catch (Exception exception)
            {
                failures.Add(exception.ToString());
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new DesktopCheckResult(false, failures, false)));
            }
            finally
            {
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => Environment.Exit(failures.Count == 0 ? 0 : 1));
            }
            }
        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
            if (!condition()) throw new TimeoutException("The desktop UI check timed out.");
        }

        private static int CountDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            var count = 0;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T) count++;
                count += CountDescendants<T>(child);
            }
            return count;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match) return match;
                if (FindDescendant<T>(child) is { } descendant) return descendant;
            }
            return default;
        }

        private static void Check(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }

        private sealed record DesktopCheckResult(bool Passed, IReadOnlyList<string> Failures, bool IsWorktree);
        }
}
