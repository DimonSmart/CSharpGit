using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async Task RunCommitGraphViewportLifecycleCheckAsync(List<string> failures)
    {
        ShowAllHistory();
        await WaitUntilAsync(
            () => HistoryList.ActualHeight > 0 && HistoryList.ActualWidth > 0,
            TimeSpan.FromSeconds(10));

        await RunDesktopDensityCheckAsync(failures);

        var originalDetailsHeight = HistoryPane.RowDefinitions[3].Height;
        var initialCount = _viewModel.History.Count;
        try
        {
            Check(initialCount >= 80, "graph viewport fixture is too small to exercise ListView recycling", failures);

            for (var cycle = 0; cycle < 6; cycle++)
            {
                HistoryPane.RowDefinitions[3].Height = new GridLength(cycle % 2 == 0 ? 520 : 140);
                await Task.Delay(120);

                var indexes = new[]
                {
                    0,
                    Math.Min(_viewModel.History.Count - 1, 35),
                    Math.Min(_viewModel.History.Count - 1, 70),
                    Math.Min(_viewModel.History.Count - 1, 10),
                };

                foreach (var index in indexes)
                {
                    if (index < 0 || index >= _viewModel.History.Count) continue;
                    var row = _viewModel.History[index];
                    HistoryList.ScrollIntoView(row);
                    await Task.Delay(80);

                    if (HistoryList.ContainerFromItem(row) is not ListViewItem container)
                    {
                        failures.Add($"history row {index} was not realized after scrolling");
                        continue;
                    }

                    var graph = FindDescendant<CommitGraphControl>(container);
                    Check(graph is not null, $"history row {index} has no commit graph control", failures);
                    Check(graph?.HasCurrentRenderForCheck() == true,
                        $"history row {index} did not paint its current graph after viewport recycle", failures);
                }
            }

            if (_viewModel.HasMore)
            {
                var beforeLoadMore = _viewModel.History.Count;
                if (_viewModel.LoadMoreCommand is AsyncCommand loadMore)
                    await loadMore.ExecuteAsync();
                Check(_viewModel.History.Count > beforeLoadMore, "Load more did not append history during graph viewport check", failures);
            }

            if (_viewModel.History.FirstOrDefault() is { } first)
            {
                HistoryList.ScrollIntoView(first);
                await Task.Delay(80);
                Check(HistoryList.ContainerFromItem(first) is ListViewItem firstContainer
                      && FindDescendant<CommitGraphControl>(firstContainer)?.HasCurrentRenderForCheck() == true,
                    "commit graph was stale after returning to the top of history", failures);
            }
        }
        finally
        {
            HistoryPane.RowDefinitions[3].Height = originalDetailsHeight;
        }
    }

    private async Task RunDesktopDensityCheckAsync(List<string> failures)
    {
        ShowAllHistory();
        if (_viewModel.History.FirstOrDefault() is { } firstHistory)
        {
            HistoryList.ScrollIntoView(firstHistory);
            await WaitUntilAsync(
                () => HistoryList.ContainerFromItem(firstHistory) is ListViewItem,
                TimeSpan.FromSeconds(5));
            CheckActualHeight(HistoryList.ContainerFromItem(firstHistory) as FrameworkElement, 23, 26, "history row", failures);
        }

        if (_repositoryTreeRoots.FirstOrDefault() is { } firstRepositoryNode)
        {
            await WaitUntilAsync(
                () => RepositoryTree.ContainerFromItem(firstRepositoryNode) is TreeViewItem,
                TimeSpan.FromSeconds(5));
            CheckActualHeight(RepositoryTree.ContainerFromItem(firstRepositoryNode) as FrameworkElement, 23, 26, "repository tree row", failures);
        }

        CheckActualHeight(MainToolbar, 0, 34, "main toolbar", failures);
        CheckActualHeight(HistoryFilterToolbar, 0, 34, "history filter toolbar", failures);
        CheckActualHeight(HistoryColumnHeader, 0, 26, "history column header", failures);
        CheckActualHeight(StatusBar, 0, 22, "status bar", failures);

        var pivotHeader = FindDescendant<PivotHeaderItem>(DetailsTabs);
        CheckActualHeight(pivotHeader, 0, 28, "details tab header", failures);

        var fullyVisibleHistoryRows = CountFullyVisibleListRows(HistoryList, _viewModel.History.Cast<object>());
        Check(fullyVisibleHistoryRows >= 20,
            $"history viewport shows only {fullyVisibleHistoryRows} fully visible rows; expected at least 20",
            failures);

        var originalDetailsTab = DetailsTabs.SelectedIndex;
        try
        {
            DetailsTabs.SelectedIndex = 1;
            await WaitUntilAsync(
                () => _changedFileTreeRoots.Count > 0 && ChangedFilesTree.ActualHeight > 0,
                TimeSpan.FromSeconds(10));

            if (_changedFileTreeRoots.FirstOrDefault() is { } firstChangedFile)
            {
                await WaitUntilAsync(
                    () => ChangedFilesTree.ContainerFromItem(firstChangedFile) is TreeViewItem,
                    TimeSpan.FromSeconds(5));
                CheckActualHeight(ChangedFilesTree.ContainerFromItem(firstChangedFile) as FrameworkElement, 23, 26, "changed files tree row", failures);
            }

            CheckActualHeight(ChangedFilesHeader, 0, 26, "changed files header", failures);

            if (_compactDiffLines.FirstOrDefault() is { } firstDiffLine)
            {
                CompactDiffList.ScrollIntoView(firstDiffLine);
                await WaitUntilAsync(
                    () => CompactDiffList.ContainerFromItem(firstDiffLine) is ListViewItem,
                    TimeSpan.FromSeconds(5));
                CheckActualHeight(CompactDiffList.ContainerFromItem(firstDiffLine) as FrameworkElement, 19, 21, "diff row", failures);
            }
        }
        finally
        {
            DetailsTabs.SelectedIndex = originalDetailsTab;
        }

        ShowWorkingTree();
        await WaitUntilAsync(
            () => WorkingTreePane.ActualHeight > 0 && _unstagedChanges.Count > 0,
            TimeSpan.FromSeconds(5));

        var stagedByCheck = false;
        try
        {
            if (_unstagedChanges.FirstOrDefault() is { } firstUnstaged)
            {
                UnstagedChangesList.SelectedItem = firstUnstaged;
                UnstagedChangesList.ScrollIntoView(firstUnstaged);
                await WaitUntilAsync(
                    () => UnstagedChangesList.ContainerFromItem(firstUnstaged) is ListViewItem,
                    TimeSpan.FromSeconds(5));
                CheckActualHeight(UnstagedChangesList.ContainerFromItem(firstUnstaged) as FrameworkElement, 23, 26, "unstaged row", failures);

                if (_stagedChanges.Count == 0)
                {
                    await Task.Delay(20);
                    await ExecuteCommandAsync(_viewModel.StageSelectedCommand);
                    await WaitUntilAsync(() => !_viewModel.IsBusy, TimeSpan.FromSeconds(20));
                    RefreshPresentationCollections();
                    await WaitUntilAsync(() => _stagedChanges.Count > 0, TimeSpan.FromSeconds(5));
                    stagedByCheck = true;
                }
            }

            if (_stagedChanges.FirstOrDefault() is { } firstStaged)
            {
                StagedChangesList.SelectedItem = firstStaged;
                StagedChangesList.ScrollIntoView(firstStaged);
                await WaitUntilAsync(
                    () => StagedChangesList.ContainerFromItem(firstStaged) is ListViewItem,
                    TimeSpan.FromSeconds(5));
                CheckActualHeight(StagedChangesList.ContainerFromItem(firstStaged) as FrameworkElement, 23, 26, "staged row", failures);
            }
        }
        finally
        {
            if (stagedByCheck && _stagedChanges.FirstOrDefault() is { } staged)
            {
                StagedChangesList.SelectedItem = staged;
                await Task.Delay(20);
                await ExecuteCommandAsync(_viewModel.UnstageSelectedCommand);
                await WaitUntilAsync(() => !_viewModel.IsBusy, TimeSpan.FromSeconds(20));
                RefreshPresentationCollections();
            }

            ShowAllHistory();
        }
    }

    private static void CheckActualHeight(
        FrameworkElement? element,
        double minimum,
        double maximum,
        string surface,
        ICollection<string> failures)
    {
        if (element is null)
        {
            failures.Add($"{surface} was not realized");
            return;
        }

        var height = element.ActualHeight;
        Check(height >= minimum && height <= maximum,
            $"{surface} actual height was {height:0.##}; expected {minimum:0.##}..{maximum:0.##}",
            failures);
    }

    private static int CountFullyVisibleListRows(ListView list, IEnumerable<object> items)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (list.ContainerFromItem(item) is not ListViewItem container) continue;

            Point topLeft;
            try
            {
                topLeft = container.TransformToVisual(list).TransformPoint(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (topLeft.Y >= -0.5 && topLeft.Y + container.ActualHeight <= list.ActualHeight + 0.5)
                count++;
        }

        return count;
    }
}
