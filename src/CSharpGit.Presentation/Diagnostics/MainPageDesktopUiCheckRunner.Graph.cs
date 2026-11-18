using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Diagnostics;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private sealed partial class DesktopUiCheckRunner
    {
        private async Task RunCommitGraphViewportLifecycleCheckAsync(List<string> failures)
        {
            await _page.ShowAllHistoryAsync();
            await WaitUntilAsync(
                () => _page.HistoryList.ActualHeight > 0 && _page.HistoryList.ActualWidth > 0,
                TimeSpan.FromSeconds(10));

            var originalDetailsHeight = _page.HistoryPane.RowDefinitions[3].Height;
            var originalItemTemplate = _page.HistoryList.ItemTemplate;
            try
            {
                await CheckInitialHistoryGraphAsync(failures);
                _page.HistoryList.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["HistoryTextGraphItemTemplate"];
                while (_page._viewModel.History.Rows.Count < 300 && _page._viewModel.History.HasMore)
                {
                    if (_page._viewModel.History.LoadMoreCommand is not AsyncCommand preload)
                        break;
                    await preload.ExecuteAsync();
                }

                Check(_page._viewModel.History.Rows.Count >= 300,
                    $"graph viewport fixture loaded only {_page._viewModel.History.Rows.Count} rows; expected at least 300",
                    failures);
                await RunHistoryPerformanceCaptureCheckAsync(failures);
                HistoryRenderDiagnostics.EnableForCheck();
                HistoryRenderDiagnostics.Reset();
                using var benchmarkProcess = System.Diagnostics.Process.GetCurrentProcess();
                var cpuBefore = benchmarkProcess.TotalProcessorTime;
                var scrollStopwatch = System.Diagnostics.Stopwatch.StartNew();

                for (var cycle = 0; cycle < 6; cycle++)
                {
                    _page.HistoryPane.RowDefinitions[3].Height = new GridLength(cycle % 2 == 0 ? 520 : 140);
                    await Task.Delay(120);

                    var indexes = new[]
                    {
                        0,
                        Math.Min(_page._viewModel.History.Rows.Count - 1, 75),
                        Math.Min(_page._viewModel.History.Rows.Count - 1, 160),
                        Math.Min(_page._viewModel.History.Rows.Count - 1, 260),
                        Math.Min(_page._viewModel.History.Rows.Count - 1, 20),
                    };

                    foreach (var index in indexes)
                    {
                        if (index < 0 || index >= _page._viewModel.History.Rows.Count) continue;
                        var row = _page._viewModel.History.Rows[index];
                        _page.HistoryList.ScrollIntoView(row);
                        await Task.Delay(80);

                        if (_page.HistoryList.ContainerFromItem(row) is not ListViewItem container)
                        {
                            failures.Add($"history row {index} was not realized after scrolling");
                            continue;
                        }

                        var graph = FindDescendant<CommitGraphControl>(container);
                        Check(graph is not null, $"history row {index} has no commit graph control", failures);
                        Check(graph?.HasCurrentRenderForCheck() == true,
                            $"history row {index} did not paint its current graph after viewport recycle: size={graph?.ActualWidth}x{graph?.ActualHeight}, graph={graph?.Graph is not null}", failures);
                        Check(graph?.HasCurrentClipForCheck() == true,
                            $"history row {index} graph clip does not match its render area", failures);
                        if (graph is not null)
                        {
                            var layout = _page.ActiveCommitGraphLayout;
                            Check(Math.Abs(graph.ActualWidth - layout.GraphWidth) <= 0.25,
                                $"history row {index} graph width {graph.ActualWidth:0.###} does not match session width {layout.GraphWidth:0.###}",
                                failures);
                            Check(graph.Metrics == layout.Metrics,
                                $"history row {index} did not receive current session graph metrics",
                                failures);
                        }
                        CheckAdjacentHistoryGraphSurfaces(index, failures);
                    }
                }

                if (_page._viewModel.History.HasMore)
                {
                    var beforeLoadMore = _page._viewModel.History.Rows.Count;
                    var beforeGraphWidth = _page._historyGraphLayout.GraphWidth;
                    var beforeLaneWidth = _page._historyGraphLayout.LaneWidth;
                    if (_page._viewModel.History.LoadMoreCommand is AsyncCommand loadMore)
                        await loadMore.ExecuteAsync();
                    Check(_page._viewModel.History.Rows.Count > beforeLoadMore, "Load more did not append history during graph viewport check", failures);
                    Check(_page._historyGraphLayout.GraphWidth + 0.001 >= beforeGraphWidth,
                        "Load more decreased the session graph width", failures);
                    Check(_page._historyGraphLayout.LaneWidth <= beforeLaneWidth + 0.001,
                        "Load more increased the session lane width", failures);
                }

                if (_page._viewModel.History.Rows.FirstOrDefault() is { } first)
                {
                    _page.HistoryList.ScrollIntoView(first);
                    await Task.Delay(80);
                    Check(_page.HistoryList.ContainerFromItem(first) is ListViewItem firstContainer
                          && FindDescendant<CommitGraphControl>(firstContainer)?.HasCurrentRenderForCheck() == true,
                        "commit graph was stale after returning to the top of history", failures);
                    CheckAdjacentHistoryGraphSurfaces(0, failures);
                }

                scrollStopwatch.Stop();
                var cpuDelta = benchmarkProcess.TotalProcessorTime - cpuBefore;
                var diagnostics = HistoryRenderDiagnostics.Snapshot();
                Check(diagnostics.RecursiveGraphLayoutTraversals == 0,
                    $"history scrolling performed {diagnostics.RecursiveGraphLayoutTraversals} recursive graph layout traversals",
                    failures);
                Check(diagnostics.RecursiveAvatarConfigurationTraversals == 0,
                    $"history scrolling performed {diagnostics.RecursiveAvatarConfigurationTraversals} recursive avatar configuration traversals",
                    failures);
                Check(diagnostics.ExplicitAvatarConfigurations == 0,
                    $"history scrolling explicitly reconfigured {diagnostics.ExplicitAvatarConfigurations} author avatars",
                    failures);
                Check(diagnostics.GeometryUpdateAttempts >= diagnostics.GeometryRebuilds,
                    $"geometry rebuild count {diagnostics.GeometryRebuilds} exceeded update attempts {diagnostics.GeometryUpdateAttempts}",
                    failures);
                System.Diagnostics.Trace.WriteLine(
                    $"History scripted scroll: wall={scrollStopwatch.Elapsed.TotalMilliseconds:0.###} ms, "
                    + $"cpu={cpuDelta.TotalMilliseconds:0.###} ms, "
                    + $"graph controls created={diagnostics.GraphControlsCreated}, "
                    + $"geometry attempts={diagnostics.GeometryUpdateAttempts}, rebuilds={diagnostics.GeometryRebuilds}, "
                    + $"author avatars created={diagnostics.AuthorAvatarsCreated}, "
                    + $"explicit avatar configurations={diagnostics.ExplicitAvatarConfigurations}, "
                    + $"recursive graph traversals={diagnostics.RecursiveGraphLayoutTraversals}, "
                    + $"recursive avatar traversals={diagnostics.RecursiveAvatarConfigurationTraversals}");
            }
            finally
            {
                _page.HistoryPane.RowDefinitions[3].Height = originalDetailsHeight;
                _page.HistoryList.ItemTemplate = originalItemTemplate;
            }
        }

        private async Task CheckInitialHistoryGraphAsync(ICollection<string> failures)
        {
            if (_page._viewModel.History.Rows.FirstOrDefault() is not { } first)
                return;

            var app = Microsoft.UI.Xaml.Application.Current as App;
            app?.ResizeMainWindowForCheck(1000, 900);
            try
            {
                // Exercise both production templates at a constrained width with
                // multiple long refs: natural and allocated badge plans differ.
                foreach (var templateKey in new[] { "HistoryItemTemplate", "HistoryTextGraphReferencesItemTemplate" })
                {
                    _page.HistoryList.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources[templateKey];
                    _page.HistoryList.ScrollIntoView(first);
                    await WaitUntilAsync(() =>
                        _page.HistoryList.ContainerFromItem(first) is ListViewItem container
                        && FindDescendant<CommitGraphControl>(container)?.HasCurrentRenderForCheck() == true,
                        TimeSpan.FromSeconds(5));

                    var graph = _page.HistoryList.ContainerFromItem(first) is ListViewItem item
                        ? FindDescendant<CommitGraphControl>(item) : null;
                    Check(graph?.HasCurrentRenderForCheck() == true,
                        $"{templateKey} did not paint the initial graph after reference layout settled", failures);
                    Check(graph?.ActualWidth == _page.ActiveCommitGraphLayout.GraphWidth,
                        $"{templateKey} lost the initial graph column width", failures);
                }
            }
            finally
            {
                app?.ResizeMainWindowForCheck(1400, 900);
            }
        }

        private async Task RunHistoryPerformanceCaptureCheckAsync(ICollection<string> failures)
        {
            var layout = _page.ActiveCommitGraphLayout;
            var context = new HistoryPerformanceStartContext(
                "main",
                _page._viewModel.History.Rows.Count,
                _page._viewModel.History.Rows.Count,
                100,
                "topo-order",
                _page._viewModel.History.HasMore,
                _page._recentRepositorySettings.ShowReflog,
                !string.IsNullOrWhiteSpace(_page._viewModel.History.FilterText),
                _page._viewModel.Branches.LocalBranches.Count,
                _page._viewModel.Branches.RemoteBranches.Count,
                _page._viewModel.Tags.Items.Count,
                _page._viewModel.Branches.LocalBranches.Count + _page._viewModel.Branches.RemoteBranches.Count + _page._viewModel.Tags.Items.Count,
                layout.GraphWidth,
                layout.ObservedMaxLaneCount,
                _page._recentRepositorySettings.ShowAuthorAvatars,
                _page._recentRepositorySettings.OnlineAvatarLookupEnabled,
                _page._recentRepositorySettings.HistoryRenderingMode,
                _page.RootLayout.ActualWidth,
                _page.RootLayout.ActualHeight,
                _page.XamlRoot?.RasterizationScale);

            HistoryPerformanceCaptureFiles? result = null;
            try
            {
                var started = await HistoryPerformanceDiagnostics.StartAsync(context, _page._gitCommandActivitySource, _page._authorAvatarService);
                Check(started, "History performance capture did not start", failures);
                if (!started) return;

                _page.AttachHistoryPerformanceSubscriptions();
                var indexes = new[]
                {
                    0,
                    Math.Min(_page._viewModel.History.Rows.Count - 1, 90),
                    Math.Min(_page._viewModel.History.Rows.Count - 1, 180),
                    Math.Min(_page._viewModel.History.Rows.Count - 1, 30)
                };
                foreach (var index in indexes)
                {
                    if (index < 0) continue;
                    _page.HistoryList.ScrollIntoView(_page._viewModel.History.Rows[index]);
                    await Task.Delay(90);
                }
            }
            finally
            {
                _page.DetachHistoryPerformanceSubscriptions();
                if (HistoryPerformanceDiagnostics.IsCaptureActive)
                    result = await HistoryPerformanceDiagnostics.StopAsync(HistoryPerformanceStopReason.Manual);
            }

            if (result is not { } files)
            {
                failures.Add("History performance capture did not produce output files");
                return;
            }

            Check(File.Exists(files.JsonlPath), "History performance JSONL was not created", failures);
            Check(File.Exists(files.SummaryPath), "History performance text summary was not created", failures);
            if (!File.Exists(files.JsonlPath) || !File.Exists(files.SummaryPath))
                return;

            var lines = await File.ReadAllLinesAsync(files.JsonlPath);
            Check(lines.Length >= 2, "History performance JSONL did not contain start and summary records", failures);
            foreach (var line in lines)
            {
                try
                {
                    using var _ = System.Text.Json.JsonDocument.Parse(line);
                }
                catch (System.Text.Json.JsonException exception)
                {
                    failures.Add($"History performance JSONL contains an invalid record: {exception.Message}");
                    break;
                }
            }

            if (lines.Length > 0)
            {
                using var last = System.Text.Json.JsonDocument.Parse(lines[^1]);
                Check(
                    last.RootElement.TryGetProperty("type", out var type)
                    && string.Equals(type.GetString(), "sessionSummary", StringComparison.Ordinal),
                    "History performance JSONL does not end with sessionSummary",
                    failures);
            }

            var summary = await File.ReadAllTextAsync(files.SummaryPath);
            Check(summary.Contains("Rendering", StringComparison.Ordinal), "History performance summary has no rendering statistics", failures);
            Check(summary.Contains("Virtualization", StringComparison.Ordinal), "History performance summary has no virtualization statistics", failures);
            Check(summary.Contains("Commit graph", StringComparison.Ordinal), "History performance summary has no graph statistics", failures);
            Check(summary.Contains("Topology converter", StringComparison.Ordinal), "History performance summary has no topology statistics", failures);
            Check(summary.Contains("Runtime", StringComparison.Ordinal), "History performance summary has no runtime statistics", failures);
            Check(summary.Contains("Git", StringComparison.Ordinal), "History performance summary has no Git statistics", failures);

            try
            {
                File.Delete(files.JsonlPath);
                File.Delete(files.SummaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void CheckAdjacentHistoryGraphSurfaces(int anchorIndex, ICollection<string> failures)
        {
            const double tolerance = 0.25;
            var checkedPair = false;
            var firstIndex = Math.Max(0, anchorIndex - 2);
            var lastIndex = Math.Min(_page._viewModel.History.Rows.Count - 2, anchorIndex + 2);

            for (var index = firstIndex; index <= lastIndex; index++)
            {
                var firstRow = _page._viewModel.History.Rows[index];
                var secondRow = _page._viewModel.History.Rows[index + 1];
                if (_page.HistoryList.ContainerFromItem(firstRow) is not ListViewItem firstContainer
                    || _page.HistoryList.ContainerFromItem(secondRow) is not ListViewItem secondContainer)
                {
                    continue;
                }

                var firstGraph = FindDescendant<CommitGraphControl>(firstContainer);
                var secondGraph = FindDescendant<CommitGraphControl>(secondContainer);
                if (firstGraph is null || secondGraph is null)
                {
                    continue;
                }

                Point firstGraphTop;
                Point secondGraphTop;
                Point firstContainerTop;
                Point secondContainerTop;
                try
                {
                    firstGraphTop = firstGraph.TransformToVisual(_page.HistoryList).TransformPoint(new Point(0, 0));
                    secondGraphTop = secondGraph.TransformToVisual(_page.HistoryList).TransformPoint(new Point(0, 0));
                    firstContainerTop = firstContainer.TransformToVisual(_page.HistoryList).TransformPoint(new Point(0, 0));
                    secondContainerTop = secondContainer.TransformToVisual(_page.HistoryList).TransformPoint(new Point(0, 0));
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                checkedPair = true;
                Check(Math.Abs(firstGraphTop.Y - firstContainerTop.Y) <= tolerance,
                    $"history graph row {index} starts {firstGraphTop.Y - firstContainerTop.Y:0.###} px below its row surface",
                    failures);
                Check(Math.Abs(firstGraph.ActualHeight - firstContainer.ActualHeight) <= tolerance,
                    $"history graph row {index} height {firstGraph.ActualHeight:0.###} does not fill row height {firstContainer.ActualHeight:0.###}",
                    failures);
                Check(Math.Abs(secondGraphTop.Y - secondContainerTop.Y) <= tolerance,
                    $"history graph row {index + 1} starts {secondGraphTop.Y - secondContainerTop.Y:0.###} px below its row surface",
                    failures);
                Check(Math.Abs(secondGraph.ActualHeight - secondContainer.ActualHeight) <= tolerance,
                    $"history graph row {index + 1} height {secondGraph.ActualHeight:0.###} does not fill row height {secondContainer.ActualHeight:0.###}",
                    failures);

                var gap = secondGraphTop.Y - (firstGraphTop.Y + firstGraph.ActualHeight);
                Check(Math.Abs(gap) <= tolerance,
                    $"history graph surfaces between rows {index} and {index + 1} have a vertical gap of {gap:0.###} px",
                    failures);
            }

            Check(checkedPair,
                $"no adjacent realized history rows were available around row {anchorIndex} for graph continuity verification",
                failures);
        }

        private async Task RunDesktopDensityCheckAsync(List<string> failures)
        {
            await _page.ShowAllHistoryAsync();
            if (_page._viewModel.History.Rows.FirstOrDefault() is { } firstHistory)
            {
                _page.HistoryList.ScrollIntoView(firstHistory);
                await WaitUntilAsync(
                    () => _page.HistoryList.ContainerFromItem(firstHistory) is ListViewItem,
                    TimeSpan.FromSeconds(5));
                CheckActualHeight(_page.HistoryList.ContainerFromItem(firstHistory) as FrameworkElement, 23, 26, "history row", failures);
            }

            if (_page.RepositoryExplorer.RootsForCheck.FirstOrDefault() is { } firstRepositoryNode)
            {
                await WaitUntilAsync(
                    () => _page.RepositoryTree.ContainerFromItem(firstRepositoryNode) is TreeViewItem,
                    TimeSpan.FromSeconds(5));
                CheckActualHeight(_page.RepositoryTree.ContainerFromItem(firstRepositoryNode) as FrameworkElement, 23, 26, "repository tree row", failures);
            }

            CheckActualHeight(_page.RepositoryToolbar, 0, 34, "main toolbar", failures);
            CheckActualHeight(_page.HistoryFilterToolbar, 0, 34, "history filter toolbar", failures);
            CheckActualHeight(_page.HistoryColumnHeader, 0, 26, "history column header", failures);
            CheckActualHeight(_page.GitConsoleHost.StatusBar, 0, 22, "status bar", failures);

            var pivotHeader = FindDescendant<PivotHeaderItem>(_page.DetailsTabs);
            CheckActualHeight(pivotHeader, 0, 28, "details tab header", failures);

            var fullyVisibleHistoryRows = CountFullyVisibleListRows(_page.HistoryList, _page._viewModel.History.Rows.Cast<object>());
            Check(fullyVisibleHistoryRows >= 20,
                $"history viewport shows only {fullyVisibleHistoryRows} fully visible rows; expected at least 20",
                failures);

            var originalDetailsTab = _page.DetailsTabs.SelectedIndex;
            try
            {
                _page.DetailsTabs.SelectedIndex = 1;
                await WaitUntilAsync(
                    () => _page._changedFileTreeRoots.Count > 0 && _page.ChangedFilesTree.ActualHeight > 0,
                    TimeSpan.FromSeconds(10));

                if (_page._changedFileTreeRoots.FirstOrDefault() is { } firstChangedFile)
                {
                    await WaitUntilAsync(
                        () => _page.ChangedFilesTree.ContainerFromItem(firstChangedFile) is TreeViewItem,
                        TimeSpan.FromSeconds(5));
                    CheckActualHeight(_page.ChangedFilesTree.ContainerFromItem(firstChangedFile) as FrameworkElement, 23, 26, "changed files tree row", failures);
                }

                CheckActualHeight(_page.ChangedFilesHeader, 0, 26, "changed files header", failures);

                if (!string.IsNullOrEmpty(_page.CompactDiffViewer.LogicalText))
                {
                    await WaitUntilAsync(
                        () => _page.CompactDiffViewer.FirstRealizedRow is not null,
                        TimeSpan.FromSeconds(5));
                    CheckActualHeight(_page.CompactDiffViewer.FirstRealizedRow, 19, 21, "diff row", failures);
                }
            }
            finally
            {
                _page.DetailsTabs.SelectedIndex = originalDetailsTab;
            }

            _page.ShowWorkingTree();
            await WaitUntilAsync(
                () => _page.WorkingTreePane.ActualHeight > 0 && WorkingTreeTreeSelection.GetLeaves(_page._unstagedTreeRoots).Count > 0,
                TimeSpan.FromSeconds(5));

            var stagedByCheck = false;
            try
            {
                if (WorkingTreeTreeSelection.GetLeaves(_page._unstagedTreeRoots).FirstOrDefault() is { } firstUnstaged)
                {
                    _page.WorkingTreeNodeInvoked(firstUnstaged, CSharpGit.Application.Abstractions.WorkingTreeDiffKind.Unstaged);
                    _page.UnstagedChangesTree.ScrollIntoView(firstUnstaged);
                    await WaitUntilAsync(
                        () => _page.UnstagedChangesTree.ContainerFromItem(firstUnstaged) is TreeViewItem,
                        TimeSpan.FromSeconds(5));
                    CheckActualHeight(_page.UnstagedChangesTree.ContainerFromItem(firstUnstaged) as FrameworkElement, 23, 26, "unstaged row", failures);

                    if (WorkingTreeTreeSelection.GetLeaves(_page._stagedTreeRoots).Count == 0)
                    {
                        await Task.Delay(20);
                        await _page.ExecuteCommandAsync(_page._viewModel.WorkingTree.StageSelectedCommand);
                        await WaitUntilAsync(() => !_page._viewModel.IsBusy, TimeSpan.FromSeconds(20));
                        _page.RefreshPresentationCollections();
                        _page.RebuildWorkingTreeTrees();
                        await WaitUntilAsync(
                            () => WorkingTreeTreeSelection.GetLeaves(_page._stagedTreeRoots).Count > 0,
                            TimeSpan.FromSeconds(5));
                        stagedByCheck = true;
                    }
                }

                if (WorkingTreeTreeSelection.GetLeaves(_page._stagedTreeRoots).FirstOrDefault() is { } firstStaged)
                {
                    _page.WorkingTreeNodeInvoked(firstStaged, CSharpGit.Application.Abstractions.WorkingTreeDiffKind.Staged);
                    _page.StagedChangesTree.ScrollIntoView(firstStaged);
                    await WaitUntilAsync(
                        () => _page.StagedChangesTree.ContainerFromItem(firstStaged) is TreeViewItem,
                        TimeSpan.FromSeconds(5));
                    CheckActualHeight(_page.StagedChangesTree.ContainerFromItem(firstStaged) as FrameworkElement, 23, 26, "staged row", failures);
                }
            }
            finally
            {
                if (stagedByCheck && WorkingTreeTreeSelection.GetLeaves(_page._stagedTreeRoots).FirstOrDefault() is { } staged)
                {
                    _page.WorkingTreeNodeInvoked(staged, CSharpGit.Application.Abstractions.WorkingTreeDiffKind.Staged);
                    await Task.Delay(20);
                    await _page.ExecuteCommandAsync(_page._viewModel.WorkingTree.UnstageSelectedCommand);
                    await WaitUntilAsync(() => !_page._viewModel.IsBusy, TimeSpan.FromSeconds(20));
                    _page.RefreshPresentationCollections();
                    _page.RebuildWorkingTreeTrees();
                }

                if (WorkingTreeTreeSelection.GetLeaves(_page._unstagedTreeRoots).FirstOrDefault() is { } restoredUnstaged)
                    _page.WorkingTreeNodeInvoked(restoredUnstaged, CSharpGit.Application.Abstractions.WorkingTreeDiffKind.Unstaged);

                await _page.ShowAllHistoryAsync();
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
}
