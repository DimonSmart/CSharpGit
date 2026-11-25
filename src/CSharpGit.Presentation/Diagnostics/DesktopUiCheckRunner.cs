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

internal sealed partial class DesktopUiCheckRunner(DesktopUiCheckContext context)
{
    private readonly DesktopUiCheckContext _context = context;

        public async Task RunAsync(string resultPath)
        {
            var failures = new List<string>();
            var busyObserved = false;
            void ObserveBusy(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
            {
                if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy) && _context.Repository.IsBusy)
                    busyObserved = true;
            }
            _context.Repository.PropertyChanged += ObserveBusy;
            try
            {
                Check(ReferenceEquals(_context.Root.DataContext, _context.Repository),
                    "generated MainPage did not receive its repository DataContext", failures);
                await _context.Repository.OpenRepositoryAsyncForDesktopCheck();
                _context.RefreshPresentationCollections();
                Check(_context.Repository.Repository is not null, $"repository did not open: {_context.Repository.ErrorMessage}", failures);
                for (var attempt = 0; attempt < 3 && _context.Repository.Repository is not null &&
                     (_context.Repository.WorkingTree.Changes.Count == 0 || _context.Repository.History.Rows.Count == 0); attempt++)
                {
                    await Task.Delay(100 * (attempt + 1));
                    await _context.Repository.RefreshAsyncForDesktopCheck();
                    _context.RefreshPresentationCollections();
                }
                Check(_context.Repository.WorkingTree.Changes.Count > 0, $"working tree changes were not loaded: {_context.Repository.ErrorMessage}", failures);
                Check(_context.Repository.History.Rows.Count > 0, $"history was not loaded: {_context.Repository.ErrorMessage}", failures);
                await WaitUntilAsync(
                    () => _context.RepositoryWorkspace.ActualWidth > 0 && _context.RepositoryWorkspace.ActualHeight > 0 && _context.HistoryView.List.ActualWidth > 0 && _context.HistoryView.List.ActualHeight > 0,
                    TimeSpan.FromSeconds(10));
                Check(_context.Repository.Repository is not null, "repository did not open", failures);
                Check(_context.RepositoryWorkspace.Visibility == Visibility.Visible,
                    "generated MainPage did not update its repository visibility binding", failures);
                Check(_context.Repository.Repository?.IsWorktree == (Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_WORKTREE") == "1"), "repository kind is incorrect", failures);
                Check(_context.Repository.History.Scopes.All(scope => scope.Label is "All references" or "Current branch"), "English history scopes are missing", failures);

                if (Environment.GetEnvironmentVariable("CSHARPGIT_GRAPH_VIEWPORT_CHECK") == "1")
                {
                    await RunCommitGraphViewportLifecycleCheckAsync(failures);
                    return;
                }

                var settings = _context.Settings;
                await settings.SetThemeModeAsync(ApplicationThemeMode.System);
                await WaitUntilAsync(() => _context.RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
                Check(_context.RequestedTheme == ElementTheme.Default, "System theme did not clear the root override", failures);

                await settings.SetThemeModeAsync(ApplicationThemeMode.Light);
                await WaitUntilAsync(() => _context.RequestedTheme == ElementTheme.Light, TimeSpan.FromSeconds(5));
                Check(_context.RequestedTheme == ElementTheme.Light, "Light theme was not applied to the main root", failures);

                _context.SettingsWindow.Open(SettingsSection.General);
                await WaitUntilAsync(() => _context.SettingsWindow.CurrentPage is { RequestedTheme: ElementTheme.Light }, TimeSpan.FromSeconds(5));
                Check(_context.SettingsWindow.CurrentPage is { RequestedTheme: ElementTheme.Light }, "new settings root did not receive the current theme", failures);

                var originalCommitTimeMode = settings.CommitTimeDisplayMode;
                var alternateCommitTimeMode = originalCommitTimeMode == CommitTimeDisplayMode.Smart
                    ? CommitTimeDisplayMode.Relative
                    : CommitTimeDisplayMode.Smart;
                await settings.SetCommitTimeDisplayModeAsync(alternateCommitTimeMode);
                Check(_context.RequestedTheme == ElementTheme.Light && _context.SettingsWindow.CurrentPage is { RequestedTheme: ElementTheme.Light }, "unrelated settings change desynchronized the theme", failures);
                await settings.SetCommitTimeDisplayModeAsync(originalCommitTimeMode);

                await settings.SetThemeModeAsync(ApplicationThemeMode.Dark);
                await WaitUntilAsync(
                    () => _context.RequestedTheme == ElementTheme.Dark && _context.SettingsWindow.CurrentPage is { RequestedTheme: ElementTheme.Dark },
                    TimeSpan.FromSeconds(5));
                Check(_context.RequestedTheme == ElementTheme.Dark, "Dark theme was not applied to the main root", failures);
                Check(_context.SettingsWindow.CurrentPage is { RequestedTheme: ElementTheme.Dark }, "Dark theme was not propagated to the settings root", failures);

                _context.SettingsWindow.CloseCurrent();
                await settings.SetThemeModeAsync(ApplicationThemeMode.System);
                await WaitUntilAsync(() => _context.RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
                Check(_context.RequestedTheme == ElementTheme.Default, "Dark to System did not clear the main root override", failures);

                Check(_context.RepositoryWorkspace.ActualWidth > 0 && _context.RepositoryWorkspace.ActualHeight > 0, "workspace was not laid out", failures);
                Check(_context.HistoryView.List.ActualWidth > 0 && _context.HistoryView.List.ActualHeight > 0, "history list was not laid out", failures);
                Check(_context.RepositoryTree.ActualWidth > 0 && _context.RepositoryExplorer.RootsForCheck.Count >= 4, "repository tree was not laid out", failures);
                Check(_context.DetailsScroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled && _context.DetailsScroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "detail message wrapping is not configured", failures);
                await WaitUntilAsync(
                    () => _context.CommitDetailsContent is { ActualWidth: > 0 } && _context.DetailsScroller.ViewportWidth > 0,
                    TimeSpan.FromSeconds(5));
                var commitMessageText = _context.CommitDetailsContent is null ? null : FindDescendant<TextBlock>(_context.CommitDetailsContent);
                var historyOrigin = _context.HistoryPane.TransformToVisual(null).TransformPoint(default);
                var availableDetailsWidth = ((Microsoft.UI.Xaml.Application.Current as App)?.MainWindowClientWidth ?? 0) /
                                            (_context.XamlRoot?.RasterizationScale ?? 1) - historyOrigin.X;
                Check(_context.CommitDetailsContent is not null && _context.CommitDetailsContent.ActualWidth <= availableDetailsWidth + 1, "commit details exceed the visible window viewport", failures);
                Check(
                    commitMessageText is not null && commitMessageText.ActualHeight > commitMessageText.FontSize * 2,
                    $"long commit message did not wrap to multiple lines: viewport={_context.DetailsScroller.ViewportWidth}, details={_context.CommitDetailsContent?.ActualWidth}, text={commitMessageText?.ActualWidth}x{commitMessageText?.ActualHeight}, font={commitMessageText?.FontSize}, length={commitMessageText?.Text.Length}, window={(Microsoft.UI.Xaml.Application.Current as App)?.MainWindowClientWidth}, scale={_context.XamlRoot?.RasterizationScale}, historyOrigin={historyOrigin.X}",
                    failures);
                if (Microsoft.UI.Xaml.Application.Current is App resizeCheckApp && commitMessageText is not null)
                {
                    var wideMessageHeight = commitMessageText.ActualHeight;
                    var messageLeft = commitMessageText.TransformToVisual(_context.HistoryPane).TransformPoint(default).X;
                    resizeCheckApp.ResizeMainWindowForCheck(700, 900);
                    await WaitUntilAsync(
                        () => resizeCheckApp.MainWindowClientWidth < 800 &&
                              _context.CommitDetailsContent!.ActualWidth < availableDetailsWidth - 200 &&
                              commitMessageText.ActualHeight > wideMessageHeight,
                        TimeSpan.FromSeconds(5));
                    var narrowMessageHeight = commitMessageText.ActualHeight;
                    Check(Math.Abs(commitMessageText.TransformToVisual(_context.HistoryPane).TransformPoint(default).X - messageLeft) <= 1,
                        "commit message moved away from its left edge after shrinking the window", failures);
                    resizeCheckApp.ResizeMainWindowForCheck(1400, 900);
                    await WaitUntilAsync(
                        () => _context.CommitDetailsContent!.ActualWidth >= availableDetailsWidth - 1 &&
                              commitMessageText.ActualHeight < narrowMessageHeight,
                        TimeSpan.FromSeconds(5));
                    Check(Math.Abs(commitMessageText.TransformToVisual(_context.HistoryPane).TransformPoint(default).X - messageLeft) <= 1,
                        "commit message moved away from its left edge after expanding the window", failures);
                }
                Check(CountDescendants<Controls.GridSplitter>(_context.RootLayout) >= 2, "resizable splitters are missing", failures);
                Check(CountDescendants<ScrollViewer>(_context.RootLayout) > 0, "scroll viewers are missing", failures);
                var xamlRoot = _context.XamlRoot;
                Check(xamlRoot is not null && (_context.RootLayout.Clip is not null || _context.RootLayout.ActualWidth <= xamlRoot.Size.Width + 1), "root content exceeds its viewport", failures);
                var splitter = FindDescendant<Controls.GridSplitter>(_context.RootLayout);
                var splitterGrid = splitter?.Parent as Grid;
                var oldWidth = splitterGrid is null ? 0 : splitterGrid.ColumnDefinitions[0].ActualWidth;
                splitter?.ResizeForCheck(24);
                Check(splitterGrid is not null && splitterGrid.ColumnDefinitions[0].Width.IsAbsolute && Math.Abs(splitterGrid.ColumnDefinitions[0].Width.Value - oldWidth) > 1, "splitter did not resize its pane", failures);

                Check(_context.RepositoryToolbar.CommitNavigationLabel == $"Commit ({_context.Repository.WorkingTree.Changes.Count})", "commit toolbar count is incorrect", failures);
                _context.Navigation.ShowWorkingTree();
                Check(_context.WorkingTreePane.Visibility == Visibility.Visible && _context.HistoryPane.Visibility == Visibility.Collapsed, "working tree mode did not open", failures);
                Check(_context.RepositoryToolbar.CommitNavigationLabel == "Back to history", "commit toolbar did not become the history navigation action", failures);
                Check(_context.WorkingTreePane.ProjectedChangeCount >= _context.Repository.WorkingTree.Changes.Count, "working tree staged/unstaged views lost changes", failures);
                await _context.Navigation.ShowAllHistoryAsync();
                Check(_context.RepositoryToolbar.CommitNavigationLabel == $"Commit ({_context.Repository.WorkingTree.Changes.Count})", "commit toolbar did not restore the change count", failures);

                if (_context.Repository.History.Rows.FirstOrDefault() is { } selectedBeforeRefresh)
                {
                    _context.Repository.History.SelectedRow = selectedBeforeRefresh;
                    var selectedHash = selectedBeforeRefresh.Commit.Hash;
                    await _context.Repository.RefreshAsyncForDesktopCheck();
                    Check(_context.Repository.History.SelectedRow?.Commit.Hash == selectedHash, "history refresh did not preserve the selected commit", failures);
                    Check(_context.Repository.History.Rows.Any(row => ReferenceEquals(row, _context.Repository.History.SelectedRow)), "history refresh kept a stale selected row instance", failures);
                }

                if (_context.Repository.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent) is { } currentBranch)
                {
                    await _context.Navigation.ShowReferenceHistoryAsync(currentBranch.Name, $"Branch: {currentBranch.Name}");
                    Check(_context.Repository.History.Rows.Any(row => ReferenceEquals(row, _context.Repository.History.SelectedRow)), "scoped history selection is not part of the current ItemsSource", failures);
                    await _context.Navigation.ShowAllHistoryAsync();
                    Check(_context.Repository.History.SelectedRow is null || _context.Repository.History.Rows.Any(row => ReferenceEquals(row, _context.Repository.History.SelectedRow)), "all-history selection is not part of the current ItemsSource", failures);
                }

                await CheckRepositoryFilesTabAsync(failures);
                await CheckInitialGraphForWorkspaceAsync(failures);

                _context.Repository.CommitCreation.CommitMessage = "draft retained by close guard";
                Check(_context.Repository.CommitCreation.HasUnappliedCommitMessage, "commit draft close guard is inactive", failures);
                if (_context.Repository.WorkingTree.Changes.FirstOrDefault(change => change.IsUnstaged) is { } workingTreeCheckChange)
                    await _context.Repository.WorkingTree.SelectChangeAsync(workingTreeCheckChange, WorkingTreeDiffKind.Unstaged);
                await WaitUntilAsync(() => !_context.Repository.IsBusy, TimeSpan.FromSeconds(20));
                Check(_context.Repository.WorkingTree.StageCommand.CanExecute(null), "Stage must be enabled for an unstaged selection", failures);
                Check(!_context.Repository.WorkingTree.UnstageCommand.CanExecute(null), "Unstage must be disabled for an unstaged selection", failures);
                var selectedChange = _context.Repository.WorkingTree.SelectedChange;
                _context.Repository.WorkingTree.ClearActiveSelection();
                Check(!_context.Repository.WorkingTree.StageCommand.CanExecute(null) && !_context.Repository.WorkingTree.UnstageCommand.CanExecute(null), "file commands must be disabled without a selection", failures);
                if (selectedChange is not null)
                    await _context.Repository.WorkingTree.SelectChangeAsync(selectedChange, WorkingTreeDiffKind.Unstaged);
                await WaitUntilAsync(() => !_context.Repository.IsBusy, TimeSpan.FromSeconds(20));
                Check(_context.Repository.CommitCreation.CommitCommand.CanExecute(null), "Commit must be enabled for a non-empty draft", failures);
                _context.Repository.CommitCreation.CommitCommand.Execute(null);
                await WaitUntilAsync(() => !_context.Repository.IsBusy, TimeSpan.FromSeconds(20));
                Check(_context.Repository.CommitCreation.IsEmptyIndexChoiceOpen, "empty index did not present an explicit choice", failures);
                Check(_context.Repository.CommitCreation.CommitMessage == "draft retained by close guard", "commit draft was lost", failures);
                Check(busyObserved && !_context.RepositoryToolbar.IsRefreshBusy, "busy indication did not transition back to idle", failures);

            }
            catch (Exception exception)
            {
                failures.Add(exception.ToString());
            }
            finally
            {
                _context.Repository.PropertyChanged -= ObserveBusy;
                var isWorktree = _context.Repository.Repository?.IsWorktree ?? false;
                try
                {
                    // The fixture owns this draft; never leave its confirmation
                    // dialog or a Git operation active when requesting native close.
                    await _context.DismissActiveDialogAsync();
                    if (_context.Repository.CommitCreation.IsEmptyIndexChoiceOpen)
                        await _context.ExecuteCommandAsync(_context.Repository.CommitCreation.CancelCommitCommand);
                    _context.Repository.CommitCreation.CommitMessage = string.Empty;
                    await WaitUntilAsync(() => !_context.Repository.IsBusy, TimeSpan.FromSeconds(20));
                }
                catch (Exception exception)
                {
                    failures.Add($"Desktop check cleanup failed: {exception}");
                }

                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(
                    new DesktopCheckResult(failures.Count == 0, failures, isWorktree)));
                Console.WriteLine($"DesktopUiCheck: Result written; failures={failures.Count}");
                foreach (var failure in failures)
                    Console.Error.WriteLine(failure);
                Environment.ExitCode = failures.Count == 0 ? 0 : 1;
                // Let the real window/host lifecycle finish instead of terminating
                // the native rendering thread with Environment.Exit.
                if (Microsoft.UI.Xaml.Application.Current is not App app)
                    throw new InvalidOperationException("The desktop check has no application host.");
                app.RequestDesktopUiCheckClose();
            }
            }
        private async Task CheckRepositoryFilesTabAsync(List<string> failures)
        {
            var tabs = _context.DetailsTabs;
            var files = tabs.Items.OfType<PivotItem>().SingleOrDefault(item => item.Name == "FilesTab");
            Check(files?.Content is RepositoryFilesSurface, "declarative Repository Files tab has no configured surface", failures);
            if (files?.Content is not RepositoryFilesSurface surface) return;
            var previous = tabs.SelectedItem;
            try
            {
                tabs.SelectedItem = files;
                await WaitUntilAsync(() => surface.IsRepositoryFilesActive && surface.FileTree.ActualWidth > 0 &&
                    surface.FileTree.ItemsSource is IEnumerable<RepositorySnapshotTreeNode> roots &&
                    roots.Any(), TimeSpan.FromSeconds(15));
                Check(surface.FileTree.ActualWidth > 0, "Repository Files tree was not laid out", failures);
                Check(surface.FileTree.ItemsSource is IEnumerable<RepositorySnapshotTreeNode> items &&
                    items.Any(node => node.Entry?.Path == "tracked.txt"),
                    "Repository Files did not load the committed fixture file", failures);
            }
            finally
            {
                tabs.SelectedItem = previous;
            }
            Check(!surface.IsRepositoryFilesActive, "Repository Files remained active after switching tabs", failures);
        }

        private async Task CheckInitialGraphForWorkspaceAsync(List<string> failures)
        {
            var originalTemplate = _context.HistoryView.List.ItemTemplate;
            try { await CheckInitialHistoryGraphAsync(failures); }
            finally { _context.HistoryView.List.ItemTemplate = originalTemplate; }
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
