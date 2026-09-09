using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async Task RunCommitGraphViewportLifecycleCheckAsync(List<string> failures)
    {
        var preloadProbe = new CommitGraphControl
        {
            Graph = new CommitGraphRowVisual(
                NodeLane: 0,
                NodeTrackId: 0,
                LaneCount: 2,
                IncomingSegments: [],
                OutgoingSegments:
                [
                    new CommitGraphSegment(0, 0, 0),
                    new CommitGraphSegment(0, 1, 1),
                ])
        };
        Check(preloadProbe.Children.Count == 3,
            "detached commit graph did not build primitives before its first layout pass", failures);
        Check(preloadProbe.HasCurrentRenderForCheck(),
            "detached commit graph preload does not match its current graph", failures);

        ShowAllHistory();
        await WaitUntilAsync(
            () => HistoryList.ActualHeight > 0 && HistoryList.ActualWidth > 0,
            TimeSpan.FromSeconds(10));

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
                        $"history row {index} retained stale commit graph primitives after viewport recycle", failures);
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
}
