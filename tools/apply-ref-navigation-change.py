from pathlib import Path


def read_normalized(path: str) -> tuple[Path, str, str]:
    file = Path(path)
    raw = file.read_bytes().decode("utf-8")
    eol = "\r\n" if "\r\n" in raw else "\n"
    return file, raw.replace("\r\n", "\n"), eol


def write_normalized(file: Path, text: str, eol: str) -> None:
    if eol == "\r\n":
        text = text.replace("\n", "\r\n")
    file.write_bytes(text.encode("utf-8"))


def replace_exact(path: str, old: str, new: str) -> None:
    file, text, eol = read_normalized(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}: {old[:100]!r}")
    write_normalized(file, text.replace(old, new), eol)


def append_before(path: str, marker: str, addition: str) -> None:
    file, text, eol = read_normalized(path)
    count = text.count(marker)
    if count != 1:
        raise RuntimeError(f"Expected exactly one marker in {path}, found {count}: {marker!r}")
    write_normalized(file, text.replace(marker, addition + marker), eol)


# History API: expose an efficient way to materialize the all-reference prefix through a target commit.
replace_exact(
    "src/CSharpGit.Application/Abstractions/IHistoryService.cs",
    """    Task<HistoryPage> ReadHistoryAsync(Repository repository, HistoryQuery query, CancellationToken cancellationToken = default);\n    Task<CommitDetails> ReadCommitAsync(Repository repository, string hash, CancellationToken cancellationToken = default);\n""",
    """    Task<HistoryPage> ReadHistoryAsync(Repository repository, HistoryQuery query, CancellationToken cancellationToken = default);\n    Task<HistoryPage> ReadHistoryThroughCommitAsync(\n        Repository repository,\n        HistoryScope scope,\n        string targetHash,\n        int trailingCount = 100,\n        CancellationToken cancellationToken = default);\n    Task<CommitDetails> ReadCommitAsync(Repository repository, string hash, CancellationToken cancellationToken = default);\n""",
)

replace_exact(
    "src/CSharpGit.Git/GitReferenceHistoryService.cs",
    """    public Task<HistoryPage> ReadHistoryAsync(\n        Repository repository,\n        string reference,\n        string? filter,\n        int skip,\n        int take = 100,\n        CancellationToken cancellationToken = default)\n""",
    """    public async Task<HistoryPage> ReadHistoryThroughCommitAsync(\n        Repository repository,\n        HistoryScope scope,\n        string targetHash,\n        int trailingCount = 100,\n        CancellationToken cancellationToken = default)\n    {\n        ArgumentNullException.ThrowIfNull(repository);\n        ValidateCommitHash(targetHash);\n        if (trailingCount is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(trailingCount));\n\n        var revisions = scope == HistoryScope.AllReferences ? new[] { \"--all\" } : new[] { \"HEAD\" };\n        var targetIndex = await FindCommitIndexAsync(repository, revisions, targetHash, cancellationToken);\n        if (targetIndex < 0)\n            throw new InvalidOperationException($\"Commit {targetHash} is not reachable from the selected history scope.\");\n\n        var requestedCount = checked(targetIndex + 1 + trailingCount);\n        var commits = await ReadHistoryPrefixAsync(repository, checked(requestedCount + 1), revisions, cancellationToken);\n        var hasMore = commits.Count > requestedCount;\n        var rows = BuildTopology(commits.Take(requestedCount).ToList());\n        return new HistoryPage(rows, hasMore);\n    }\n\n    public Task<HistoryPage> ReadHistoryAsync(\n        Repository repository,\n        string reference,\n        string? filter,\n        int skip,\n        int take = 100,\n        CancellationToken cancellationToken = default)\n""",
)

replace_exact(
    "src/CSharpGit.Git/GitReferenceHistoryService.cs",
    """    private async Task<List<CommitHistoryItem>> ReadHistoryThroughAsync(\n""",
    """    private async Task<int> FindCommitIndexAsync(\n        Repository repository,\n        IReadOnlyList<string> revisions,\n        string targetHash,\n        CancellationToken cancellationToken)\n    {\n        var maxCount = 256;\n        while (true)\n        {\n            var arguments = new List<string>\n            {\n                \"log\",\n                \"--topo-order\",\n                $\"--max-count={maxCount}\",\n                \"--format=%H\"\n            };\n            arguments.AddRange(revisions);\n\n            var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, arguments.ToArray());\n            var hashes = output\n                .Split(['\\r', '\\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)\n                .ToList();\n            var targetIndex = hashes.FindIndex(hash => string.Equals(hash, targetHash, StringComparison.Ordinal));\n            if (targetIndex >= 0) return targetIndex;\n            if (hashes.Count < maxCount || maxCount == int.MaxValue) return -1;\n\n            maxCount = (int)Math.Min((long)maxCount * 2, int.MaxValue);\n        }\n    }\n\n    private async Task<List<CommitHistoryItem>> ReadHistoryThroughAsync(\n""",
)

# Keep the large view model maintainable: switch it to partial and isolate reference navigation.
replace_exact(
    "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs",
    "public sealed class OpenRepositoryViewModel : INotifyPropertyChanged\n",
    "public sealed partial class OpenRepositoryViewModel : INotifyPropertyChanged\n",
)

Path("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.ReferenceNavigation.cs").write_text(
    """using CSharpGit.Domain;\n\nnamespace CSharpGit.Presentation.ViewModels;\n\npublic sealed partial class OpenRepositoryViewModel\n{\n    internal async Task<HistoryRow?> EnsureHistoryCommitVisibleAsync(\n        string hash,\n        int trailingCount = 100)\n    {\n        if (Repository is null) return null;\n\n        var repository = Repository;\n        var requiresReload = _selectedScope != Scopes[0] || !string.IsNullOrWhiteSpace(_filterText);\n\n        if (_selectedScope != Scopes[0])\n        {\n            _selectedScope = Scopes[0];\n            Notify(nameof(SelectedScope));\n        }\n\n        if (!string.IsNullOrEmpty(_filterText))\n        {\n            _filterText = string.Empty;\n            Notify(nameof(FilterText));\n        }\n\n        if (!requiresReload && History.FirstOrDefault(row =>\n                string.Equals(row.Commit.Hash, hash, StringComparison.Ordinal)) is { } existing)\n        {\n            SelectedHistoryRow = existing;\n            return existing;\n        }\n\n        var generation = Interlocked.Increment(ref _historyLoadGeneration);\n        var cancellation = new CancellationTokenSource();\n        var previousCancellation = Interlocked.Exchange(ref _historyLoadCts, cancellation);\n        if (previousCancellation is not null)\n        {\n            previousCancellation.Cancel();\n            previousCancellation.Dispose();\n        }\n\n        EnterBusy();\n        ErrorMessage = null;\n        try\n        {\n            var page = await _historyService.ReadHistoryThroughCommitAsync(\n                repository,\n                HistoryScope.AllReferences,\n                hash,\n                trailingCount,\n                cancellation.Token);\n\n            if (cancellation.IsCancellationRequested\n                || generation != Volatile.Read(ref _historyLoadGeneration)\n                || !ReferenceEquals(repository, Repository))\n                return null;\n\n            Replace(History, page.Rows);\n            HasMore = page.HasMore;\n\n            var target = History.FirstOrDefault(row =>\n                string.Equals(row.Commit.Hash, hash, StringComparison.Ordinal));\n            if (target is null)\n                throw new InvalidOperationException($\"Commit {hash} was not present after history navigation load.\");\n\n            SelectedHistoryRow = target;\n            return target;\n        }\n        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)\n        {\n            return null;\n        }\n        catch (Exception exception) when (exception is not OperationCanceledException)\n        {\n            if (generation == Volatile.Read(ref _historyLoadGeneration))\n            {\n                ErrorMessage = $\"Could not navigate to commit: {exception.Message}\";\n                _logger.LogWarning(exception, \"History reference navigation failed for {CommitHash}\", hash);\n            }\n            return null;\n        }\n        finally\n        {\n            ExitBusy();\n        }\n    }\n}\n""",
    encoding="utf-8",
    newline="\n",
)

# Repository-tree single click navigates the shared all-reference graph instead of replacing it.
replace_exact(
    "src/CSharpGit.Presentation/MainPage.xaml.cs",
    """    private async Task ShowReferenceHistoryAsync(string reference, string label)\n""",
    """    private async Task NavigateToReferenceAsync(string commitHash)\n    {\n        _referenceHistoryCts?.Cancel();\n        _activeReference = null;\n        ScopeCombo.Visibility = Visibility.Visible;\n        ReferenceScopePanel.Visibility = Visibility.Collapsed;\n        HistoryPane.Visibility = Visibility.Visible;\n        WorkingTreePane.Visibility = Visibility.Collapsed;\n        HistoryList.ItemsSource = _viewModel.History;\n\n        var target = await _viewModel.EnsureHistoryCommitVisibleAsync(commitHash);\n        HistoryList.ItemsSource = _viewModel.History;\n        LoadMoreHistoryButton.IsEnabled = _viewModel.HasMore;\n        if (target is null) return;\n\n        HistoryList.SelectedItem = target;\n        HistoryList.ScrollIntoView(target, ScrollIntoViewAlignment.Leading);\n    }\n\n    private async Task ShowReferenceHistoryAsync(string reference, string label)\n""",
)

replace_exact(
    "src/CSharpGit.Presentation/MainPage.xaml.cs",
    """            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch local:\n                _viewModel.SelectedLocalBranch = local;\n                await ShowReferenceHistoryAsync(local.Name, $\"Branch: {local.Name}\");\n                break;\n            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:\n                _viewModel.SelectedRemoteBranch = remoteBranch;\n                await ShowReferenceHistoryAsync(remoteBranch.Name, $\"Remote: {remoteBranch.Name}\");\n                break;\n            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:\n                _viewModel.SelectedTag = tag;\n                await ShowReferenceHistoryAsync(tag.Name, $\"Tag: {tag.Name}\");\n                break;\n            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:\n                _viewModel.SelectedStash = stash;\n                await ShowReferenceHistoryAsync(stash.Commit, $\"Stash: {stash.Name}\");\n                break;\n""",
    """            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch local:\n                _viewModel.SelectedLocalBranch = local;\n                await NavigateToReferenceAsync(local.Commit);\n                break;\n            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:\n                _viewModel.SelectedRemoteBranch = remoteBranch;\n                await NavigateToReferenceAsync(remoteBranch.Commit);\n                break;\n            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:\n                _viewModel.SelectedTag = tag;\n                await NavigateToReferenceAsync(tag.Commit);\n                break;\n            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:\n                _viewModel.SelectedStash = stash;\n                await NavigateToReferenceAsync(stash.Commit);\n                break;\n""",
)

replace_exact(
    "src/CSharpGit.Presentation/MainPage.xaml.cs",
    """                AddMenuItem(flyout, \"Delete\", !branch.IsCurrent && !_viewModel.IsBusy, async () =>\n                {\n                    _viewModel.SelectedLocalBranch = branch;\n                    await ExecuteCommandAsync(_viewModel.DeleteBranchCommand);\n                });\n                flyout.Items.Add(new MenuFlyoutSeparator());\n                AddMenuItem(flyout, \"Copy branch name\", true, () => CopyTextAsync(branch.Name));\n""",
    """                AddMenuItem(flyout, \"Delete\", !branch.IsCurrent && !_viewModel.IsBusy, async () =>\n                {\n                    _viewModel.SelectedLocalBranch = branch;\n                    await ExecuteCommandAsync(_viewModel.DeleteBranchCommand);\n                });\n                flyout.Items.Add(new MenuFlyoutSeparator());\n                AddMenuItem(flyout, \"Show branch history only\", !_viewModel.IsBusy,\n                    () => ShowReferenceHistoryAsync(branch.Name, $\"Branch: {branch.Name}\"));\n                AddMenuItem(flyout, \"Copy branch name\", true, () => CopyTextAsync(branch.Name));\n""",
)

replace_exact(
    "src/CSharpGit.Presentation/MainPage.xaml.cs",
    """            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:\n                AddMenuItem(flyout, \"Checkout as tracking branch\", !_viewModel.IsBusy, async () =>\n                {\n                    _viewModel.SelectedRemoteBranch = remoteBranch;\n                    var slash = remoteBranch.Name.IndexOf('/');\n                    _viewModel.NewBranchName = slash >= 0 ? remoteBranch.Name[(slash + 1)..] : remoteBranch.Name;\n                    await ExecuteCommandAsync(_viewModel.CheckoutRemoteCommand);\n                });\n                AddMenuItem(flyout, \"Copy branch name\", true, () => CopyTextAsync(remoteBranch.Name));\n                break;\n""",
    """            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:\n                AddMenuItem(flyout, \"Checkout as tracking branch\", !_viewModel.IsBusy, async () =>\n                {\n                    _viewModel.SelectedRemoteBranch = remoteBranch;\n                    var slash = remoteBranch.Name.IndexOf('/');\n                    _viewModel.NewBranchName = slash >= 0 ? remoteBranch.Name[(slash + 1)..] : remoteBranch.Name;\n                    await ExecuteCommandAsync(_viewModel.CheckoutRemoteCommand);\n                });\n                AddMenuItem(flyout, \"Show branch history only\", !_viewModel.IsBusy,\n                    () => ShowReferenceHistoryAsync(remoteBranch.Name, $\"Remote: {remoteBranch.Name}\"));\n                AddMenuItem(flyout, \"Copy branch name\", true, () => CopyTextAsync(remoteBranch.Name));\n                break;\n""",
)

# Durable intent: tree refs navigate the shared graph; ref-only history remains an explicit secondary action.
replace_exact(
    ".idd/intent/IDD-0010.spec-history-first-main-workspace.md",
    """Выбор local branch, remote branch, tag или stash показывает history выбранной\nref без обязательного checkout. Активная ref явно отображается над history.\nDouble click local branch выполняет Switch/Checkout.\n""",
    """Выбор local branch, remote branch, tag или stash по умолчанию является навигацией\nв общей `All references` history: приложение находит commit, на который указывает\nref, при необходимости догружает общий history prefix до этого commit и переводит\nselection/viewport на соответствующую строку без checkout. Контекст общей topology\nпри этом сохраняется. Отдельный ref-scoped history остается доступным как явное\nконтекстное действие `Show branch history only` (или эквивалентное для другой ref).\nDouble click local branch выполняет Switch/Checkout.\n""",
)

replace_exact(
    ".idd/intent/IDD-0010.spec-history-first-main-workspace.md",
    """Общий scope как минимум поддерживает `All references` и `Current branch`.\nВыбор конкретной ref в Repository Tree использует ref-scoped history, не меняя\nworking branch.\n""",
    """Общий scope как минимум поддерживает `All references` и `Current branch`.\nОбычный выбор конкретной ref в Repository Tree переключает presentation на\n`All references` (если нужен другой scope), снимает несовместимый text filter,\nобеспечивает наличие target commit в incrementally materialized общей history и\nнавигационно выделяет этот commit. Ref-scoped history включается только отдельным\nявным действием и не меняет working branch.\n""",
)

replace_exact(
    ".idd/intent/IDD-0010.spec-history-first-main-workspace.md",
    """- Ref-scoped history является read-only Git operation и не требует checkout.\n""",
    """- Навигация к ref в общей history и optional ref-scoped history являются read-only Git operations и не требуют checkout.\n- Для target commit вне текущего loaded prefix допускается быстрый lightweight поиск его позиции по hash с последующей загрузкой общего topology prefix до target плюс небольшой trailing context; не требуется последовательная загрузка страниц по 100 commits.\n""",
)

replace_exact(
    ".idd/intent/IDD-0010.spec-history-first-main-workspace.md",
    """- Выбор branch/ref показывает её history без обязательного checkout.\n""",
    """- Single click branch/ref сохраняет общий `All references` graph, при необходимости догружает его до target commit, выбирает этот commit и прокручивает его в viewport без checkout.\n- Даже старая ref, target commit которой не входил в первоначально загруженный history prefix, остается доступной для навигации и не должна становиться disabled/grey только из-за lazy loading.\n- Ref-only history доступна отдельным явным contextual action и не является default single-click behavior.\n""",
)

# Integration coverage: target is older than the normal first 100-row page and still resolves in one navigation request.
Path("tests/CSharpGit.Git.Tests/GitReferenceNavigationTests.cs").write_text(
    """using System.Diagnostics;\nusing CSharpGit.Domain;\n\nnamespace CSharpGit.Git.Tests;\n\npublic sealed class GitReferenceNavigationTests : IDisposable\n{\n    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $\"csharpgit-ref-nav-{Guid.NewGuid():N}\");\n\n    [Fact]\n    public async Task ReadsAllReferencesThroughOldTargetWithTrailingContext()\n    {\n        InitializeRepository();\n        string targetHash = string.Empty;\n        for (var index = 0; index < 110; index++)\n        {\n            RunGit(\"commit\", \"--allow-empty\", \"-m\", $\"commit-{index}\");\n            if (index == 5)\n            {\n                targetHash = RunGit(\"rev-parse\", \"HEAD\");\n                RunGit(\"branch\", \"stale/old\", targetHash);\n            }\n        }\n\n        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);\n        var service = new GitReferenceHistoryService();\n        var firstPage = await service.ReadHistoryAsync(\n            repository,\n            new HistoryQuery(HistoryScope.AllReferences, null, 0, 100));\n\n        Assert.DoesNotContain(firstPage.Rows, row => row.Commit.Hash == targetHash);\n\n        var navigationPage = await service.ReadHistoryThroughCommitAsync(\n            repository,\n            HistoryScope.AllReferences,\n            targetHash,\n            trailingCount: 3);\n\n        var targetIndex = navigationPage.Rows.ToList().FindIndex(row => row.Commit.Hash == targetHash);\n        Assert.True(targetIndex >= 100);\n        Assert.True(navigationPage.Rows.Count >= targetIndex + 1);\n        Assert.Equal(\"commit-5\", navigationPage.Rows[targetIndex].Commit.Subject);\n        Assert.Contains(navigationPage.Rows, row => row.Commit.Subject == \"commit-4\");\n        Assert.True(navigationPage.HasMore);\n        Assert.Equal(\"main\", RunGit(\"branch\", \"--show-current\"));\n    }\n\n    private void InitializeRepository()\n    {\n        Directory.CreateDirectory(_temporaryDirectory);\n        RunGit(\"init\", \"-b\", \"main\");\n        RunGit(\"config\", \"user.email\", \"tests@example.invalid\");\n        RunGit(\"config\", \"user.name\", \"CSharpGit Tests\");\n    }\n\n    private string RunGit(params string[] arguments)\n    {\n        var startInfo = new ProcessStartInfo(\"git\")\n        {\n            WorkingDirectory = _temporaryDirectory,\n            RedirectStandardOutput = true,\n            RedirectStandardError = true,\n            UseShellExecute = false,\n            CreateNoWindow = true\n        };\n        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);\n        using var process = Process.Start(startInfo)!;\n        var output = process.StandardOutput.ReadToEnd();\n        var error = process.StandardError.ReadToEnd();\n        process.WaitForExit();\n        if (process.ExitCode != 0) throw new InvalidOperationException(error);\n        return output.Trim();\n    }\n\n    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);\n}\n""",
    encoding="utf-8",
    newline="\n",
)

print("Reference navigation change applied.")
