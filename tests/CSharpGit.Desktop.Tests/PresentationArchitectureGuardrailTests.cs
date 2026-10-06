using System.Text.RegularExpressions;

namespace CSharpGit.Desktop.Tests;

public sealed class PresentationArchitectureGuardrailTests
{
    [Fact]
    public void ProductionSourceDoesNotReintroduceGlobalApplicationServiceAccess()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var forbidden = new[]
        {
            "AppSettingsContext.Current",
            "RepositoryImageServices.Current",
            "GitCommandActivitySession.Current",
            "GitCommandExecutor.Default"
        };

        foreach (var file in ProductionCsFiles(sourceRoot))
        {
            var source = File.ReadAllText(file);
            foreach (var pattern in forbidden)
                Assert.DoesNotContain(pattern, source, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "AppSettingsContext.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "RepositoryImageServices.cs")));
    }

    [Fact]
    public void PresentationCreatesApplicationWideServicesOnlyInCompositionRoot()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var appPath = Path.Combine(presentation, "App.xaml.cs");
        var app = File.ReadAllText(appPath);

        foreach (var file in ProductionCsFiles(presentation).Where(path => !Path.GetFullPath(path).Equals(Path.GetFullPath(appPath), StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("new JsonAppSettingsService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new RepositoryImageService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitCommandActivityHistory", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetRequiredService<", source, StringComparison.Ordinal);
        }

        Assert.Contains("new JsonAppSettingsService()", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IRepositoryImageService, RepositoryImageService>()", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<GitCommandActivityHistory>()", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageHasOnePublicConstructionPathAndNoExternalInitializationProtocol()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);

        Assert.Single(Regex.Matches(combined, @"\bpublic\s+MainPage\s*\(").Cast<Match>());
        Assert.DoesNotContain("mainPage.Initialize", app, StringComparison.Ordinal);
        Assert.DoesNotContain("MainPageServices", combined, StringComparison.Ordinal);
        Assert.Contains("SettingsWindowController", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageRepositoryCollectionSubscriptionsAreNamedAndSymmetricallyDetached()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPage = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(presentation, "MainPage.Lifecycle.cs"));

        var subscriptions = new[]
        {
            ("WorkingTree.Changes", "RepositoryPresentationChanges_CollectionChanged"),
            ("Branches.LocalBranches", "RepositoryPresentationLocalBranches_CollectionChanged"),
            ("Branches.RemoteBranches", "RepositoryPresentationRemoteBranches_CollectionChanged"),
            ("Remotes", "RepositoryPresentationRemotes_CollectionChanged"),
            ("Tags", "RepositoryPresentationTags_CollectionChanged"),
            ("Stashes", "RepositoryPresentationStashes_CollectionChanged")
        };

        foreach (var (collection, handler) in subscriptions)
        {
            Assert.Contains($"_viewModel.{collection}.CollectionChanged += {handler};", mainPage, StringComparison.Ordinal);
            Assert.Contains($"_viewModel.{collection}.CollectionChanged -= {handler};", lifecycle, StringComparison.Ordinal);
            Assert.DoesNotContain($"_viewModel.{collection}.CollectionChanged += (_, _) =>", mainPage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainPageDoesNotOwnTagApplicationService()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);
        var tagsViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "TagsViewModel.cs"));

        Assert.DoesNotContain("ITagService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_tagService", combined, StringComparison.Ordinal);
        Assert.Contains("public sealed class TagsViewModel", tagsViewModel, StringComparison.Ordinal);
        Assert.Contains("ITagService", tagsViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageDoesNotOwnWorktreeApplicationService()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);
        var worktreesViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "WorktreesViewModel.cs"));

        Assert.DoesNotContain("IWorktreeService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_worktreeService", combined, StringComparison.Ordinal);
        Assert.Contains("public sealed class WorktreesViewModel", worktreesViewModel, StringComparison.Ordinal);
        Assert.Contains("IWorktreeService", worktreesViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageDoesNotOwnRepositoryFilesApplicationService()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);
        var repositoryFilesViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "RepositoryFilesViewModel.cs"));

        Assert.DoesNotContain("IRepositorySnapshotService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositorySnapshotService", combined, StringComparison.Ordinal);
        Assert.Contains("public sealed class RepositoryFilesViewModel", repositoryFilesViewModel, StringComparison.Ordinal);
        Assert.Contains("IRepositorySnapshotService", repositoryFilesViewModel, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", repositoryFilesViewModel, StringComparison.Ordinal);
        Assert.Contains("CanPublishSnapshot", repositoryFilesViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageDoesNotOwnWorkingTreeApplicationServices()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);
        var workingTreeViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "WorkingTreeViewModel.cs"));

        Assert.DoesNotContain("IWorkingTreeService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_workingTreeService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("IWorkingTreeDiffService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_workingTreeDiffService", combined, StringComparison.Ordinal);
        Assert.Contains("public sealed class WorkingTreeViewModel", workingTreeViewModel, StringComparison.Ordinal);
        Assert.Contains("IWorkingTreeService", workingTreeViewModel, StringComparison.Ordinal);
        Assert.Contains("IWorkingTreeDiffService", workingTreeViewModel, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", workingTreeViewModel, StringComparison.Ordinal);
        Assert.Contains("CanPublishDiff", workingTreeViewModel, StringComparison.Ordinal);
        Assert.False(File.Exists(
            Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.Discard.cs")));
    }

    [Fact]
    public void BranchMutationOrchestrationBelongsToBranchesViewModel()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);
        var branchesViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "BranchesViewModel.cs"));
        var openRepositoryViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.cs"));

        var forbiddenMainPageCalls = new[]
        {
            "_referenceService.CreateBranchAsync(",
            "_referenceService.SwitchBranchAsync(",
            "_referenceService.RenameBranchAsync(",
            "_referenceService.DeleteBranchAsync(",
            "_referenceService.CheckoutRemoteBranchAsync(",
            "_repositorySyncService.DeleteRemoteBranchAsync(",
            "_repositorySyncService.PreparePublishBranchAsync(",
            "_repositorySyncService.PublishBranchAsync("
        };

        foreach (var call in forbiddenMainPageCalls)
            Assert.DoesNotContain(call, combined, StringComparison.Ordinal);

        Assert.Contains("public sealed class BranchesViewModel", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("IReferenceService", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("IRepositorySyncService", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_referenceService.CreateBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_referenceService.SwitchBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_referenceService.RenameBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_referenceService.DeleteBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_referenceService.CheckoutRemoteBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_syncService.DeleteRemoteBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_syncService.PreparePublishBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_syncService.PublishBranchAsync(", branchesViewModel, StringComparison.Ordinal);

        Assert.Contains("public BranchesViewModel Branches { get; }", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public ObservableCollection<GitBranch> LocalBranches", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public ObservableCollection<GitBranch> RemoteBranches", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public GitBranch? SelectedLocalBranch", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public GitBranch? SelectedRemoteBranch", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public ICommand SwitchBranchCommand", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public ICommand CheckoutRemoteCommand", openRepositoryViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryPresentationLifecycleBelongsToHistoryViewModel()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combinedMainPage = string.Join(Environment.NewLine, mainPageSources);
        var historyViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "HistoryViewModel.cs"));
        var openRepositoryViewModel = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("public sealed class HistoryViewModel", historyViewModel, StringComparison.Ordinal);
        Assert.Contains("IHistoryService", historyViewModel, StringComparison.Ordinal);
        Assert.Contains("public HistoryViewModel History { get; }", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.Contains("History = historyViewModel", openRepositoryViewModel, StringComparison.Ordinal);

        Assert.DoesNotContain("IHistoryService", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_historyService", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_scopedHistory", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_referenceHistoryCts", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_scopedHasMore", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_isScopedHistoryLoading", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadScopedHistoryAsync", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("new HistoryQuery(", combinedMainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryList.ItemsSource =", combinedMainPage, StringComparison.Ordinal);

        Assert.DoesNotContain("public ObservableCollection<HistoryRow> History", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadHistoryAsync", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureHistoryCommitVisibleAsync", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            presentation,
            "ViewModels",
            "OpenRepositoryViewModel.ReferenceNavigation.cs")));
        Assert.False(File.Exists(Path.Combine(
            presentation,
            "ViewModels",
            "OpenRepositoryViewModel.Reflog.cs")));
    }

    [Fact]
    public void WindowTitleUsesPresentationStateAndShellOwnedWindow()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        var mainPage = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.cs"));
        var formatter = File.ReadAllText(Path.Combine(presentation, "MainWindowTitleFormatter.cs"));

        Assert.Contains("mainPage.WindowTitleChanged += MainPage_WindowTitleChanged;", app, StringComparison.Ordinal);
        Assert.Contains("page.WindowTitleChanged -= MainPage_WindowTitleChanged;", app, StringComparison.Ordinal);
        Assert.Contains("_window.Title = title;", app, StringComparison.Ordinal);
        Assert.Contains("CurrentHeadCommit", mainPage, StringComparison.Ordinal);
        Assert.Contains("IsDetachedHead", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Window.Title", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Xaml", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositoryStateService", formatter, StringComparison.Ordinal);
        Assert.DoesNotContain("IProcessExecutor", formatter, StringComparison.Ordinal);
        Assert.DoesNotContain("HeadDisplay", formatter, StringComparison.Ordinal);
    }

    private static IEnumerable<string> ProductionCsFiles(string root) =>
        Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
