using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace CSharpGit.Presentation;

public sealed partial class MainPage : Page
{
    private readonly OpenRepositoryViewModel _viewModel;

    public MainPage(OpenRepositoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += RunDesktopCheckWhenRequested;
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (!_viewModel.HasUnappliedCommitMessage) return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Discard commit message?",
            Content = "The commit message has not been applied. Close the window and discard it?",
            PrimaryButtonText = "Discard and close",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void RunDesktopCheckWhenRequested(object sender, RoutedEventArgs args)
    {
        var resultPath = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT");
        if (string.IsNullOrWhiteSpace(resultPath)) return;

        Loaded -= RunDesktopCheckWhenRequested;
        var failures = new List<string>();
        try
        {
            var busyObserved = false;
            _viewModel.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy) && _viewModel.IsBusy)
                    busyObserved = true;
            };

            await _viewModel.OpenRepositoryAsyncForDesktopCheck();
            Check(_viewModel.Repository is not null, $"repository did not open: {_viewModel.ErrorMessage}", failures);
            for (var attempt = 0; attempt < 3 && _viewModel.Repository is not null &&
                 (_viewModel.Changes.Count == 0 || _viewModel.History.Count == 0); attempt++)
            {
                await Task.Delay(100 * (attempt + 1));
                await _viewModel.RefreshAsyncForDesktopCheck();
            }
            Check(_viewModel.Changes.Count > 0, $"working tree changes were not loaded: {_viewModel.ErrorMessage}", failures);
            Check(_viewModel.History.Count > 0, $"history was not loaded: {_viewModel.ErrorMessage}", failures);
            await WaitUntilAsync(
                () => RepositoryWorkspace.ActualWidth > 0 && RepositoryWorkspace.ActualHeight > 0 && HistoryList.ActualWidth > 0 && HistoryList.ActualHeight > 0,
                TimeSpan.FromSeconds(10));
            Check(_viewModel.Repository is not null, "repository did not open", failures);
            Check(_viewModel.Repository?.IsWorktree == (Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_WORKTREE") == "1"), "repository kind is incorrect", failures);
            Check(_viewModel.Themes.Select(theme => theme.Label).SequenceEqual(["System", "Light", "Dark"]), "English theme labels are missing", failures);
            Check(_viewModel.Scopes.All(scope => scope.Label is "All references" or "Current branch"), "English history scopes are missing", failures);

            await DispatcherQueue.EnqueueAsync(() =>
            {
                _viewModel.SelectedTheme = ElementTheme.Light;
                _viewModel.SelectedTheme = ElementTheme.Dark;
                _viewModel.SelectedTheme = ElementTheme.Default;
            });

            Check(RepositoryWorkspace.ActualWidth > 0 && RepositoryWorkspace.ActualHeight > 0, "workspace was not laid out", failures);
            Check(HistoryList.ActualWidth > 0 && HistoryList.ActualHeight > 0, "history list was not laid out", failures);
            Check(DetailsScroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && DetailsScroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "detail scrolling is not automatic", failures);
            Check(CountDescendants<Controls.GridSplitter>(RootLayout) >= 3, "resizable splitters are missing", failures);
            Check(CountDescendants<ScrollViewer>(RootLayout) > 0, "scroll viewers are missing", failures);
            var xamlRoot = XamlRoot;
            Check(xamlRoot is not null && (RootLayout.Clip is not null || RootLayout.ActualWidth <= xamlRoot.Size.Width + 1), "root content exceeds its viewport", failures);
            var splitter = FindDescendant<Controls.GridSplitter>(RootLayout);
            var splitterGrid = splitter?.Parent as Grid;
            var oldWidth = splitterGrid is null ? 0 : splitterGrid.ColumnDefinitions[0].ActualWidth;
            splitter?.ResizeForCheck(24);
            Check(splitterGrid is not null && splitterGrid.ColumnDefinitions[0].Width.IsAbsolute && Math.Abs(splitterGrid.ColumnDefinitions[0].Width.Value - oldWidth) > 1, "splitter did not resize its pane", failures);

            _viewModel.CommitMessage = "draft retained by close guard";
            Check(_viewModel.HasUnappliedCommitMessage, "commit draft close guard is inactive", failures);
            _viewModel.SelectedChange = _viewModel.Changes.FirstOrDefault(change => change.IsUnstaged);
            Check(_viewModel.StageCommand.CanExecute(null), "Stage must be enabled for an unstaged selection", failures);
            Check(!_viewModel.UnstageCommand.CanExecute(null), "Unstage must be disabled for an unstaged selection", failures);
            var selectedChange = _viewModel.SelectedChange;
            _viewModel.SelectedChange = null;
            Check(!_viewModel.StageCommand.CanExecute(null) && !_viewModel.UnstageCommand.CanExecute(null), "file commands must be disabled without a selection", failures);
            _viewModel.SelectedChange = selectedChange;
            Check(_viewModel.CommitCommand.CanExecute(null), "Commit must be enabled for a non-empty draft", failures);
            _viewModel.CommitCommand.Execute(null);
            await WaitUntilAsync(() => !_viewModel.IsBusy, TimeSpan.FromSeconds(20));
            Check(_viewModel.IsEmptyIndexChoiceOpen, "empty index did not present an explicit choice", failures);
            Check(_viewModel.CommitMessage == "draft retained by close guard", "commit draft was lost", failures);
            Check(busyObserved && !BusyIndicator.IsActive, "busy indication did not transition back to idle", failures);

            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new DesktopCheckResult(failures.Count == 0, failures, _viewModel.Repository?.IsWorktree ?? false)));
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

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue queue, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        })) completion.SetException(new InvalidOperationException("The UI dispatcher rejected the check."));
        return completion.Task;
    }
}
