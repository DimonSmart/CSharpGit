from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    file = Path(path)
    file.parent.mkdir(parents=True, exist_ok=True)
    with file.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(text)


def replace_between(text: str, start: str, end: str, replacement: str) -> str:
    start_index = text.index(start)
    end_index = text.index(end, start_index)
    return text[:start_index] + replacement + text[end_index:]


service_path = "src/CSharpGit.Git/GitReferenceHistoryService.cs"
service = read(service_path)
history_core = r'''    private async Task<HistoryPage> ReadHistoryCoreAsync(
        Repository repository,
        string? filter,
        int skip,
        int take,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(filter))
        {
            var commits = await ReadHistoryPrefixAsync(repository, skip + take + 1, revisions, cancellationToken);
            var hasMore = commits.Count > skip + take;
            var rows = BuildTopology(commits.Take(skip + take).ToList());
            return new HistoryPage(rows.Skip(skip).ToList(), hasMore);
        }

        var matchArguments = new List<string>
        {
            "log",
            "--topo-order",
            $"--max-count={skip + take + 1}",
            "--format=%H",
            "--regexp-ignore-case",
            $"--grep={filter.Trim()}"
        };
        matchArguments.AddRange(revisions);

        var matchOutput = await RunGitAsync(repository.WorkingDirectory, cancellationToken, matchArguments.ToArray());
        var matchingHashes = matchOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var hasFilteredMore = matchingHashes.Count > skip + take;
        var requestedHashes = matchingHashes.Skip(skip).Take(take).ToList();
        if (requestedHashes.Count == 0)
            return new HistoryPage([], hasFilteredMore);

        var commitsThroughPage = await ReadHistoryThroughAsync(
            repository,
            revisions,
            requestedHashes[^1],
            cancellationToken);
        var rowsByHash = BuildTopology(commitsThroughPage)
            .ToDictionary(row => row.Commit.Hash, StringComparer.Ordinal);
        var requestedRows = requestedHashes
            .Select(hash => rowsByHash.TryGetValue(hash, out var row)
                ? row
                : throw new InvalidOperationException($"Commit {hash} was not found in the unfiltered topology traversal."))
            .ToList();

        return new HistoryPage(requestedRows, hasFilteredMore);
    }

    private async Task<List<CommitHistoryItem>> ReadHistoryThroughAsync(
        Repository repository,
        IReadOnlyList<string> revisions,
        string targetHash,
        CancellationToken cancellationToken)
    {
        var maxCount = 256;
        while (true)
        {
            var commits = await ReadHistoryPrefixAsync(repository, maxCount, revisions, cancellationToken);
            var targetIndex = commits.FindIndex(commit => string.Equals(commit.Hash, targetHash, StringComparison.Ordinal));
            if (targetIndex >= 0)
                return commits.Take(targetIndex + 1).ToList();

            if (commits.Count < maxCount)
                throw new InvalidOperationException($"Filtered commit {targetHash} was not found in the unfiltered history traversal.");

            if (maxCount == int.MaxValue)
                throw new InvalidOperationException($"History traversal is too large to resolve filtered commit {targetHash}.");

            maxCount = (int)Math.Min((long)maxCount * 2, int.MaxValue);
        }
    }

    private async Task<List<CommitHistoryItem>> ReadHistoryPrefixAsync(
        Repository repository,
        int maxCount,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "log",
            "--topo-order",
            "--date=iso-strict",
            $"--max-count={maxCount}",
            "--format=%H%x00%P%x00%an%x00%aI%x00%D%x00%B%x1e"
        };
        arguments.AddRange(revisions);
        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, arguments.ToArray());
        return ParseHistory(output).ToList();
    }

'''
service = replace_between(
    service,
    "    private async Task<HistoryPage> ReadHistoryCoreAsync(",
    "    private async Task<string> RunGitAsync",
    history_core)

topology = r'''    internal static IReadOnlyList<HistoryRow> BuildTopology(IReadOnlyList<CommitHistoryItem> commits)
    {
        var lanes = new List<LaneState>();
        var rows = new List<HistoryRow>(commits.Count);
        var nextTrackId = 0;

        foreach (var commit in commits)
        {
            var lane = lanes.FindIndex(state => state.Commit == commit.Hash);
            var laneAlreadyExisted = lane >= 0;
            if (!laneAlreadyExisted)
            {
                lane = lanes.Count;
                lanes.Add(new LaneState(commit.Hash, nextTrackId++));
            }

            var before = lanes.ToList();
            var nodeTrackId = before[lane].TrackId;
            var incoming = before
                .Select((state, index) => new TopologyEdge(index, index, state.TrackId))
                .Where(edge => laneAlreadyExisted || edge.FromLane != lane)
                .ToList();

            lanes.RemoveAt(lane);

            // Parent insertion can shift lanes that already exist. Build the complete
            // lower-boundary state first, then resolve numeric lane indexes by identity.
            for (var parentIndex = 0; parentIndex < commit.Parents.Count; parentIndex++)
            {
                var parent = commit.Parents[parentIndex];
                if (lanes.Any(state => state.Commit == parent)) continue;

                var parentLane = Math.Min(lane + parentIndex, lanes.Count);
                var trackId = parentIndex == 0 ? nodeTrackId : nextTrackId++;
                lanes.Insert(parentLane, new LaneState(parent, trackId));
            }

            var outgoing = new List<TopologyEdge>();
            for (var beforeLane = 0; beforeLane < before.Count; beforeLane++)
            {
                if (beforeLane == lane) continue;
                var state = before[beforeLane];
                var afterLane = lanes.FindIndex(candidate => candidate.Commit == state.Commit);
                if (afterLane >= 0)
                    outgoing.Add(new TopologyEdge(beforeLane, afterLane, state.TrackId));
            }

            foreach (var parent in commit.Parents)
            {
                var parentLane = lanes.FindIndex(state => state.Commit == parent);
                if (parentLane >= 0)
                    outgoing.Add(new TopologyEdge(lane, parentLane, lanes[parentLane].TrackId));
            }

            rows.Add(new HistoryRow(
                commit,
                new CommitTopology(lane, outgoing)
                {
                    NodeTrackId = nodeTrackId,
                    IncomingEdges = incoming,
                    HasExactGraphTopology = true
                }));
        }
        return rows;
    }

'''
service = replace_between(
    service,
    "    private static IReadOnlyList<HistoryRow> BuildTopology(",
    "    private static IReadOnlyList<string> ParseRefs",
    topology)
write(service_path, service)

write(
    "src/CSharpGit.Git/Properties/AssemblyInfo.cs",
    'using System.Runtime.CompilerServices;\n\n[assembly: InternalsVisibleTo("CSharpGit.Git.Tests")]\n')

vm_path = "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs"
vm = read(vm_path)
if "private CancellationTokenSource? _historyLoadCts;" not in vm:
    vm = vm.replace(
        "    private readonly SemaphoreSlim _mutationGate = new(1, 1);\n",
        "    private readonly SemaphoreSlim _mutationGate = new(1, 1);\n"
        "    private CancellationTokenSource? _historyLoadCts;\n"
        "    private long _historyLoadGeneration;\n"
        "    private long _commitLoadGeneration;\n"
        "    private long _diffLoadGeneration;\n")
vm = vm.replace(
    "    public HistoryRow? SelectedHistoryRow { get => _selectedHistoryRow; set { if (_selectedHistoryRow == value) return; _selectedHistoryRow = value; Notify(); _ = LoadCommitAsync(); } }",
    "    public HistoryRow? SelectedHistoryRow { get => _selectedHistoryRow; set { if (ReferenceEquals(_selectedHistoryRow, value)) return; _selectedHistoryRow = value; Notify(); _ = LoadCommitAsync(); } }")

load_history = r'''    private async Task LoadHistoryAsync(bool reset)
    {
        if (Repository is null) return;

        var repository = Repository;
        var selectedHash = reset ? SelectedHistoryRow?.Commit.Hash : null;
        var skip = reset ? 0 : History.Count;
        var scope = SelectedScope.Value;
        var filter = FilterText;
        var generation = Interlocked.Increment(ref _historyLoadGeneration);
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _historyLoadCts, cancellation);
        if (previousCancellation is not null)
        {
            previousCancellation.Cancel();
            previousCancellation.Dispose();
        }

        EnterBusy();
        try
        {
            var page = await _historyService.ReadHistoryAsync(
                repository,
                new HistoryQuery(scope, filter, skip),
                cancellation.Token);

            if (cancellation.IsCancellationRequested
                || generation != Volatile.Read(ref _historyLoadGeneration)
                || !ReferenceEquals(repository, Repository))
                return;

            if (reset) History.Clear();
            foreach (var row in page.Rows) History.Add(row);
            HasMore = page.HasMore;

            if (reset)
            {
                var restored = selectedHash is null
                    ? History.FirstOrDefault()
                    : History.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
                      ?? History.FirstOrDefault();
                SelectedHistoryRow = restored;
                if (restored is null)
                {
                    SelectedCommit = null;
                    SelectedFile = null;
                    SelectedDiff = null;
                }
            }
            else if (SelectedHistoryRow is null && History.FirstOrDefault() is { } first)
            {
                SelectedHistoryRow = first;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation != Volatile.Read(ref _historyLoadGeneration)) return;
            ErrorMessage = $"Could not read history: {exception.Message}";
            _logger.LogWarning(exception, "History loading failed");
        }
        finally { ExitBusy(); }
    }

'''
vm = replace_between(vm, "    private async Task LoadHistoryAsync(bool reset)", "    private async Task LoadCommitAsync()", load_history)

load_commit = r'''    private async Task LoadCommitAsync()
    {
        var repository = Repository;
        var selectedRow = SelectedHistoryRow;
        var generation = Interlocked.Increment(ref _commitLoadGeneration);
        if (repository is null || selectedRow is null)
        {
            SelectedCommit = null;
            SelectedFile = null;
            SelectedDiff = null;
            return;
        }

        try
        {
            var commit = await _historyService.ReadCommitAsync(repository, selectedRow.Commit.Hash);
            if (generation != Volatile.Read(ref _commitLoadGeneration)
                || !ReferenceEquals(repository, Repository)
                || !ReferenceEquals(selectedRow, SelectedHistoryRow))
                return;

            SelectedCommit = commit;
            SelectedFile = SelectedCommit.Files.FirstOrDefault();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _commitLoadGeneration)
                && ReferenceEquals(repository, Repository)
                && ReferenceEquals(selectedRow, SelectedHistoryRow))
                ErrorMessage = $"Could not read commit: {exception.Message}";
        }
    }

'''
vm = replace_between(vm, "    private async Task LoadCommitAsync()", "    private async Task LoadDiffAsync()", load_commit)

load_diff = r'''    private async Task LoadDiffAsync()
    {
        var repository = Repository;
        var selectedCommit = SelectedCommit;
        var selectedFile = SelectedFile;
        var generation = Interlocked.Increment(ref _diffLoadGeneration);
        if (repository is null || selectedCommit is null || selectedFile is null)
        {
            SelectedDiff = null;
            return;
        }

        try
        {
            var diff = await _historyService.ReadDiffAsync(repository, selectedCommit.Commit.Hash, selectedFile.Path);
            if (generation != Volatile.Read(ref _diffLoadGeneration)
                || !ReferenceEquals(repository, Repository)
                || !ReferenceEquals(selectedCommit, SelectedCommit)
                || !ReferenceEquals(selectedFile, SelectedFile))
                return;
            SelectedDiff = diff;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _diffLoadGeneration)
                && ReferenceEquals(repository, Repository)
                && ReferenceEquals(selectedCommit, SelectedCommit)
                && ReferenceEquals(selectedFile, SelectedFile))
                ErrorMessage = $"Could not read change: {exception.Message}";
        }
    }

'''
vm = replace_between(vm, "    private async Task LoadDiffAsync()", "    private void Notify(", load_diff)
write(vm_path, vm)

page_path = "src/CSharpGit.Presentation/MainPage.xaml.cs"
page = read(page_path)
selection_reconcile = '''        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.SelectedHistoryRow) && _activeReference is not null)\n        {\n            var selectedHash = _viewModel.SelectedHistoryRow?.Commit.Hash;\n            var visibleSelection = selectedHash is null\n                ? null\n                : _scopedHistory.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal));\n            visibleSelection ??= _scopedHistory.FirstOrDefault();\n            if (!ReferenceEquals(visibleSelection, _viewModel.SelectedHistoryRow))\n                _viewModel.SelectedHistoryRow = visibleSelection;\n        }\n'''
marker = "        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.HasMore) && _activeReference is null)\n"
if selection_reconcile not in page:
    page = page.replace(marker, selection_reconcile + marker)

scoped = r'''    private async Task LoadScopedHistoryAsync(bool reset)
    {
        if (_viewModel.Repository is null || _activeReference is null) return;

        var selectedHash = reset ? _viewModel.SelectedHistoryRow?.Commit.Hash : null;
        var skip = reset ? 0 : _scopedHistory.Count;
        if (reset)
        {
            _referenceHistoryCts?.Cancel();
            _referenceHistoryCts?.Dispose();
            _referenceHistoryCts = new CancellationTokenSource();
        }
        _referenceHistoryCts ??= new CancellationTokenSource();
        var repository = _viewModel.Repository;
        var reference = _activeReference;
        var token = _referenceHistoryCts.Token;
        _isScopedHistoryLoading = true;
        LoadMoreHistoryButton.IsEnabled = false;
        try
        {
            var page = await _referenceHistoryService.ReadHistoryAsync(
                repository,
                reference,
                _viewModel.FilterText,
                skip,
                100,
                token);
            if (token.IsCancellationRequested
                || !ReferenceEquals(repository, _viewModel.Repository)
                || !string.Equals(reference, _activeReference, StringComparison.Ordinal))
                return;

            if (reset) _scopedHistory.Clear();
            foreach (var row in page.Rows) _scopedHistory.Add(row);
            _scopedHasMore = page.HasMore;
            HistoryList.ItemsSource = _scopedHistory;

            if (reset)
            {
                var restored = selectedHash is null
                    ? _scopedHistory.FirstOrDefault()
                    : _scopedHistory.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
                      ?? _scopedHistory.FirstOrDefault();
                _viewModel.SelectedHistoryRow = restored;
            }
            else if (_viewModel.SelectedHistoryRow is null && _scopedHistory.FirstOrDefault() is { } first)
            {
                _viewModel.SelectedHistoryRow = first;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not read the selected reference history", exception.Message);
        }
        finally
        {
            _isScopedHistoryLoading = false;
            if (string.Equals(reference, _activeReference, StringComparison.Ordinal))
                LoadMoreHistoryButton.IsEnabled = _scopedHasMore;
        }
    }

'''
page = replace_between(page, "    private async Task LoadScopedHistoryAsync(bool reset)", "    private void ShowAllHistory()", scoped)

show_all = r'''    private void ShowAllHistory()
    {
        var selectedHash = _viewModel.SelectedHistoryRow?.Commit.Hash;
        _referenceHistoryCts?.Cancel();
        _activeReference = null;
        ScopeCombo.Visibility = Visibility.Visible;
        ReferenceScopePanel.Visibility = Visibility.Collapsed;
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
        HistoryList.ItemsSource = _viewModel.History;
        LoadMoreHistoryButton.IsEnabled = _viewModel.HasMore;

        var restored = selectedHash is null
            ? _viewModel.History.FirstOrDefault()
            : _viewModel.History.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
              ?? _viewModel.History.FirstOrDefault();
        _viewModel.SelectedHistoryRow = restored;

        if (_viewModel.SelectedScope != _viewModel.Scopes[0])
            _viewModel.SelectedScope = _viewModel.Scopes[0];
    }

'''
page = replace_between(page, "    private void ShowAllHistory()", "    private void ShowWorkingTree()", show_all)

smoke_marker = "            ShowAllHistory();\n\n            _viewModel.CommitMessage = \"draft retained by close guard\";"
smoke_replacement = '''            ShowAllHistory();\n\n            if (_viewModel.History.FirstOrDefault() is { } selectedBeforeRefresh)\n            {\n                _viewModel.SelectedHistoryRow = selectedBeforeRefresh;\n                var selectedHash = selectedBeforeRefresh.Commit.Hash;\n                await _viewModel.RefreshAsyncForDesktopCheck();\n                Check(_viewModel.SelectedHistoryRow?.Commit.Hash == selectedHash, \"history refresh did not preserve the selected commit\", failures);\n                Check(_viewModel.History.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow)), \"history refresh kept a stale selected row instance\", failures);\n            }\n\n            if (_viewModel.LocalBranches.FirstOrDefault(branch => branch.IsCurrent) is { } currentBranch)\n            {\n                await ShowReferenceHistoryAsync(currentBranch.Name, $\"Branch: {currentBranch.Name}\");\n                Check(_scopedHistory.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow)), \"scoped history selection is not part of the current ItemsSource\", failures);\n                ShowAllHistory();\n                Check(_viewModel.SelectedHistoryRow is null || _viewModel.History.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow)), \"all-history selection is not part of the current ItemsSource\", failures);\n            }\n\n            _viewModel.CommitMessage = \"draft retained by close guard\";'''
if "history refresh kept a stale selected row instance" not in page:
    page = page.replace(smoke_marker, smoke_replacement)
write(page_path, page)

write(
    "tests/CSharpGit.Git.Tests/GitReferenceHistoryTopologyRegressionTests.cs",
    r'''using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceHistoryTopologyRegressionTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-topology-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesParentLaneAfterAllParentInsertions()
    {
        var rows = GitReferenceHistoryService.BuildTopology(
        [
            Commit("M", "X", "L", "Q"),
            Commit("X", "Q", "N")
        ]);

        var first = rows[0];
        var qTrack = Assert.Single(first.Topology.Edges.Where(edge => edge.FromLane == first.Topology.Lane && edge.ToLane == 2)).TrackId;
        var shiftedParent = rows[1];

        Assert.Contains(shiftedParent.Topology.Edges, edge =>
            edge.FromLane == shiftedParent.Topology.Lane
            && edge.ToLane == 2
            && edge.TrackId == qTrack);
        Assert.DoesNotContain(shiftedParent.Topology.Edges, edge =>
            edge.FromLane == shiftedParent.Topology.Lane
            && edge.TrackId == qTrack
            && edge.ToLane != 2);
    }

    [Fact]
    public void PreservesTrackIdentityAcrossThreeParallelLanes()
    {
        var rows = GitReferenceHistoryService.BuildTopology(
        [
            Commit("M", "A", "B", "C"),
            Commit("A", "A1"),
            Commit("B", "B1"),
            Commit("C", "C1")
        ]);

        var branchTracks = rows[0].Topology.Edges
            .Where(edge => edge.FromLane == rows[0].Topology.Lane)
            .OrderBy(edge => edge.ToLane)
            .Select(edge => edge.TrackId)
            .ToArray();
        Assert.Equal(3, branchTracks.Distinct().Count());
        Assert.Equal(branchTracks[0], rows[1].Topology.NodeTrackId);
        Assert.Equal(branchTracks[1], rows[2].Topology.NodeTrackId);
        Assert.Equal(branchTracks[2], rows[3].Topology.NodeTrackId);
    }

    [Fact]
    public void BuildsAllEdgesForOctopusMerge()
    {
        var row = Assert.Single(GitReferenceHistoryService.BuildTopology(
        [
            Commit("M", "A", "B", "C", "D")
        ]));

        var parentEdges = row.Topology.Edges.Where(edge => edge.FromLane == row.Topology.Lane).ToArray();
        Assert.Equal(4, parentEdges.Length);
        Assert.Equal(new[] { 0, 1, 2, 3 }, parentEdges.Select(edge => edge.ToLane).Order().ToArray());
        Assert.Equal(4, parentEdges.Select(edge => edge.TrackId).Distinct().Count());
    }

    [Fact]
    public async Task FilteredRowsUseTopologyFromCompleteHistory()
    {
        InitializeMergeRepository();
        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var service = new GitReferenceHistoryService();
        var full = await service.ReadHistoryAsync(repository, "main", null, 0, 100);
        var filtered = await service.ReadHistoryAsync(repository, "main", "visible", 0, 100);

        Assert.Equal(new[] { "visible tip", "visible root" }, filtered.Rows.Select(row => row.Commit.Subject).ToArray());
        Assert.All(filtered.Rows, row => Assert.True(row.Topology.HasExactGraphTopology));
        foreach (var actual in filtered.Rows)
        {
            var expected = Assert.Single(full.Rows, row => row.Commit.Hash == actual.Commit.Hash);
            AssertTopologyEqual(expected.Topology, actual.Topology);
        }
    }

    [Fact]
    public async Task PaginationContinuesTheSameTopologyAcrossPageBoundary()
    {
        InitializeMergeRepository();
        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var service = new GitReferenceHistoryService();
        var full = await service.ReadHistoryAsync(repository, "main", null, 0, 100);
        var page1 = await service.ReadHistoryAsync(repository, "main", null, 0, 2);
        var page2 = await service.ReadHistoryAsync(repository, "main", null, 2, 2);
        var combined = page1.Rows.Concat(page2.Rows).ToArray();

        Assert.Equal(full.Rows.Take(4).Select(row => row.Commit.Hash), combined.Select(row => row.Commit.Hash));
        for (var index = 0; index < combined.Length; index++)
            AssertTopologyEqual(full.Rows[index].Topology, combined[index].Topology);
    }

    [Fact]
    public async Task FilterPaginationStillUsesCompleteTopology()
    {
        InitializeMergeRepository();
        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var service = new GitReferenceHistoryService();
        var full = await service.ReadHistoryAsync(repository, "main", null, 0, 100);
        var page1 = await service.ReadHistoryAsync(repository, "main", "visible", 0, 1);
        var page2 = await service.ReadHistoryAsync(repository, "main", "visible", 1, 1);
        var combined = page1.Rows.Concat(page2.Rows).ToArray();

        Assert.True(page1.HasMore);
        Assert.False(page2.HasMore);
        Assert.Equal(new[] { "visible tip", "visible root" }, combined.Select(row => row.Commit.Subject).ToArray());
        foreach (var actual in combined)
        {
            var expected = Assert.Single(full.Rows, row => row.Commit.Hash == actual.Commit.Hash);
            AssertTopologyEqual(expected.Topology, actual.Topology);
        }
    }

    private static CommitHistoryItem Commit(string hash, params string[] parents) =>
        new(hash, parents, hash, hash, "tests", DateTimeOffset.UnixEpoch, []);

    private static void AssertTopologyEqual(CommitTopology expected, CommitTopology actual)
    {
        Assert.Equal(expected.Lane, actual.Lane);
        Assert.Equal(expected.NodeTrackId, actual.NodeTrackId);
        Assert.Equal(expected.HasExactGraphTopology, actual.HasExactGraphTopology);
        Assert.Equal(expected.IncomingEdges.ToArray(), actual.IncomingEdges.ToArray());
        Assert.Equal(expected.Edges.ToArray(), actual.Edges.ToArray());
    }

    private void InitializeMergeRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "root.txt"), "root\n");
        RunGit("add", "root.txt");
        RunGit("commit", "-m", "visible root");

        RunGit("switch", "-c", "feature/test");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "feature.txt"), "feature\n");
        RunGit("add", "feature.txt");
        RunGit("commit", "-m", "hidden feature");

        RunGit("switch", "main");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "main.txt"), "main\n");
        RunGit("add", "main.txt");
        RunGit("commit", "-m", "hidden main");
        RunGit("merge", "--no-ff", "feature/test", "-m", "hidden merge");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "tip.txt"), "tip\n");
        RunGit("add", "tip.txt");
        RunGit("commit", "-m", "visible tip");
    }

    private string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output.Trim();
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
    }
}
''')

write(
    "tests/CSharpGit.Application.Tests/HistoryLifecycleContractTests.cs",
    r'''namespace CSharpGit.Application.Tests;

public sealed class HistoryLifecycleContractTests
{
    [Fact]
    public void HistorySelectionAndLoadingHaveStableIdentityAndStaleRequestGuards()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));

        Assert.Contains("ReferenceEquals(_selectedHistoryRow, value)", viewModel);
        Assert.Contains("SelectedHistoryRow?.Commit.Hash", viewModel);
        Assert.Contains("_historyLoadGeneration", viewModel);
        Assert.Contains("CancellationTokenSource? _historyLoadCts", viewModel);
        Assert.Contains("generation != Volatile.Read(ref _historyLoadGeneration)", viewModel);
        Assert.Contains("ReferenceEquals(selectedRow, SelectedHistoryRow)", viewModel);
        Assert.DoesNotContain("HistoryList.SelectedItem = first", page);
        Assert.Contains("_scopedHistory.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow))", page);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
''')
