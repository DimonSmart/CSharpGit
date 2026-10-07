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
            ("RepositorySync.Remotes", "RepositoryPresentationRemotes_CollectionChanged"),
            ("Tags", "RepositoryPresentationTags_CollectionChanged"),
            ("Stashes.Items", "RepositoryPresentationStashes_CollectionChanged")
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
    public void BranchAndRepositorySyncOrchestrationHaveSeparateOwners()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var viewModels = Path.Combine(presentation, "ViewModels");
        var mainPage = string.Join(
            Environment.NewLine,
            Directory.GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
        var branchesViewModel = File.ReadAllText(Path.Combine(viewModels, "BranchesViewModel.cs"));
        var repositorySync = File.ReadAllText(Path.Combine(viewModels, "RepositorySyncViewModel.cs"));
        var openRepository = File.ReadAllText(Path.Combine(viewModels, "OpenRepositoryViewModel.cs"));
        var syncContext = File.ReadAllText(Path.Combine(viewModels, "OpenRepositoryViewModel.RepositorySync.cs"));
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));

        Assert.Contains("public sealed class BranchesViewModel", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("IReferenceService", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("IRepositorySyncService", branchesViewModel, StringComparison.Ordinal);
        Assert.Contains("_syncService.DeleteRemoteBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("_syncService.PreparePublishBranchAsync(", branchesViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("_syncService.PublishBranchAsync(", branchesViewModel, StringComparison.Ordinal);

        Assert.Contains("public sealed class RepositorySyncViewModel", repositorySync, StringComparison.Ordinal);
        Assert.Contains("IRepositorySyncService", repositorySync, StringComparison.Ordinal);
        Assert.Contains("IAppSettingsService", repositorySync, StringComparison.Ordinal);
        Assert.Contains("_syncService.FetchAsync(", repositorySync, StringComparison.Ordinal);
        Assert.Contains("_syncService.PullAsync(", repositorySync, StringComparison.Ordinal);
        Assert.Contains("_syncService.PushAsync(", repositorySync, StringComparison.Ordinal);
        Assert.Contains("_syncService.PreparePublishBranchAsync(", repositorySync, StringComparison.Ordinal);
        Assert.Contains("_syncService.PublishBranchAsync(", repositorySync, StringComparison.Ordinal);
        Assert.Contains("_syncService.PrepareForcePushWithLeaseAsync(", repositorySync, StringComparison.Ordinal);
        Assert.Contains("_syncService.ForcePushWithLeaseAsync(repository, snapshot)", repositorySync, StringComparison.Ordinal);

        Assert.Contains("public RepositorySyncViewModel RepositorySync { get; }", openRepository, StringComparison.Ordinal);
        Assert.Contains("IRepositorySyncContext", syncContext, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.Remotes", File.ReadAllText(Path.Combine(viewModels, "OpenRepositoryViewModel.Branches.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositorySyncService", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public ObservableCollection<GitRemote> Remotes", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public GitRemote? SelectedRemote", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public ICommand FetchCommand", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public ICommand FetchAllCommand", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public ICommand PullCommand", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public ICommand PushCommand", openRepository, StringComparison.Ordinal);

        Assert.DoesNotContain("IRepositorySyncService", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositorySyncService", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("PushRejectedException", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ForcePushWithLeasePreparationException", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ForcePushWithLeaseCancelledException", mainPage, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient<RepositorySyncViewModel>();", app, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryLoadingBelongsToHistoryViewModel()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);
        var history = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "HistoryViewModel.cs"));
        var openRepository = File.ReadAllText(
            Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("public sealed class HistoryViewModel", history, StringComparison.Ordinal);
        Assert.Contains("IHistoryService", history, StringComparison.Ordinal);
        Assert.Contains("public HistoryViewModel History { get; }", openRepository, StringComparison.Ordinal);

        Assert.DoesNotContain("IHistoryService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_historyService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_scopedHistory", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_referenceHistoryCts", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_scopedHasMore", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_isScopedHistoryLoading", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadScopedHistoryAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("new HistoryQuery(", combined, StringComparison.Ordinal);

        Assert.DoesNotContain("public ObservableCollection<HistoryRow> History", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public HistoryRow? SelectedHistoryRow", openRepository, StringComparison.Ordinal);
        Assert.False(File.Exists(
            Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.ReferenceNavigation.cs")));
        Assert.False(File.Exists(
            Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.Reflog.cs")));
    }

    [Fact]
    public void CommitDetailsAndStashesOwnTheirPresentationState()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var viewModels = Path.Combine(presentation, "ViewModels");
        var openRepositorySources = Directory
            .GetFiles(viewModels, "OpenRepositoryViewModel*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var openRepository = string.Join(Environment.NewLine, openRepositorySources);
        var details = File.ReadAllText(Path.Combine(viewModels, "CommitDetailsViewModel.cs"));
        var stashes = File.ReadAllText(Path.Combine(viewModels, "StashesViewModel.cs"));
        var repositoryFilesSurface = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryFiles.cs"));
        var mainPage = string.Join(
            Environment.NewLine,
            Directory.GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.False(File.Exists(Path.Combine(viewModels, "OpenRepositoryViewModel.CommitChanges.cs")));
        Assert.False(File.Exists(Path.Combine(viewModels, "OpenRepositoryViewModel.Stashes.cs")));
        Assert.False(File.Exists(Path.Combine(viewModels, "OpenRepositoryViewModel.RepositoryFiles.cs")));

        Assert.DoesNotContain("IHistoryService", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("IStashService", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("IStashMutationService", openRepository, StringComparison.Ordinal);
        Assert.Contains("public StashesViewModel Stashes { get; }", openRepository, StringComparison.Ordinal);
        Assert.Contains("public CommitDetailsViewModel CommitDetails { get; }", openRepository, StringComparison.Ordinal);

        Assert.Contains("IHistoryService", details, StringComparison.Ordinal);
        Assert.Contains("IStashService", details, StringComparison.Ordinal);
        Assert.DoesNotContain("IStashMutationService", details, StringComparison.Ordinal);
        Assert.Contains("IStashMutationService", stashes, StringComparison.Ordinal);
        Assert.DoesNotContain("IHistoryService", stashes, StringComparison.Ordinal);
        Assert.DoesNotContain("IStashService", stashes, StringComparison.Ordinal);

        Assert.DoesNotContain("public ChangedFile? SelectedFile", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public FileDiff? SelectedDiff", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("public GitStash? SelectedStash", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SelectedFile", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SelectedDiff", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SelectedStashDetails", mainPage, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.Attach(_viewModel.CommitDetails);", repositoryFilesSurface, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitMutationOrchestrationBelongsToCommitActionsViewModel()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var viewModels = Path.Combine(presentation, "ViewModels");
        var mainPage = string.Join(
            Environment.NewLine,
            Directory.GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
        var commitActions = File.ReadAllText(Path.Combine(viewModels, "CommitActionsViewModel.cs"));
        var openRepository = File.ReadAllText(Path.Combine(viewModels, "OpenRepositoryViewModel.cs"));
        var contextAdapter = File.ReadAllText(Path.Combine(viewModels, "OpenRepositoryViewModel.CommitActions.cs"));
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));

        Assert.Contains("public sealed class CommitActionsViewModel", commitActions, StringComparison.Ordinal);
        Assert.Contains("ICommitActionService", commitActions, StringComparison.Ordinal);
        Assert.Contains("IReferenceService", commitActions, StringComparison.Ordinal);
        Assert.Contains("public CommitActionsViewModel CommitActions { get; }", openRepository, StringComparison.Ordinal);
        Assert.Contains("ICommitActionsRepositoryContext", contextAdapter, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient<CommitActionsViewModel>();", app, StringComparison.Ordinal);

        Assert.DoesNotContain("ICommitActionService", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("IReferenceService", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_commitActionService.", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_referenceService.CheckoutAsync(", mainPage, StringComparison.Ordinal);

        foreach (var directCall in new[]
                 {
                     "_commitActionService.CherryPickAsync",
                     "_commitActionService.RevertAsync",
                     "_commitActionService.ResetAsync",
                     "_commitActionService.FixupIntoPreviousCommitAsync"
                 })
        {
            Assert.DoesNotContain(directCall, mainPage, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Microsoft.UI.Xaml", commitActions, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", commitActions, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryViewModel", commitActions, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitCreationOwnsCommitDraftAndCreationWorkflow()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var viewModels = Path.Combine(presentation, "ViewModels");
        var openRepositorySources = string.Join(
            Environment.NewLine,
            Directory.GetFiles(viewModels, "OpenRepositoryViewModel*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
        var openRepository = File.ReadAllText(Path.Combine(viewModels, "OpenRepositoryViewModel.cs"));
        var commitCreation = File.ReadAllText(Path.Combine(viewModels, "CommitCreationViewModel.cs"));
        var contextAdapter = File.ReadAllText(
            Path.Combine(viewModels, "OpenRepositoryViewModel.CommitCreation.cs"));
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        var mainXaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var mainPage = string.Join(
            Environment.NewLine,
            Directory.GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.Contains("public sealed class CommitCreationViewModel : INotifyPropertyChanged", commitCreation, StringComparison.Ordinal);
        Assert.Contains("IWorkingTreeService", commitCreation, StringComparison.Ordinal);
        Assert.Contains("public CommitCreationViewModel CommitCreation { get; }", openRepository, StringComparison.Ordinal);
        Assert.Contains("ICommitCreationRepositoryContext", contextAdapter, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient<CommitCreationViewModel>();", app, StringComparison.Ordinal);

        Assert.DoesNotContain("IWorkingTreeService", openRepositorySources, StringComparison.Ordinal);
        Assert.DoesNotContain("_workingTreeService", openRepositorySources, StringComparison.Ordinal);
        foreach (var oldParentMember in new[]
                 {
                     "public string CommitMessage",
                     "public bool IsEmptyIndexChoiceOpen",
                     "public ICommand CommitCommand",
                     "public ICommand EmptyCommitCommand",
                     "public ICommand AmendCommand",
                     "public ICommand StageAllAndCommitCommand",
                     "public ICommand ConfirmEmptyCommitCommand",
                     "public ICommand CancelCommitCommand",
                     "private Task RequestCommitAsync",
                     "private async Task StageAllAndCommitAsync",
                     "private Task ConfirmEmptyCommitAsync",
                     "private Task CancelCommitAsync",
                     "private async Task CommitAsync"
                 })
        {
            Assert.DoesNotContain(oldParentMember, openRepositorySources, StringComparison.Ordinal);
        }

        Assert.Contains("Binding CommitCreation.CommitMessage", mainXaml, StringComparison.Ordinal);
        Assert.Contains("Binding CommitCreation.CommitCommand", mainXaml, StringComparison.Ordinal);
        Assert.Contains("Binding CommitCreation.AmendCommand", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.CommitMessage", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.CommitCommand", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.IsEmptyIndexChoiceOpen", mainPage, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CommitCreation.HasUnappliedCommitMessage", mainPage, StringComparison.Ordinal);

        Assert.DoesNotContain("Microsoft.UI.Xaml", commitCreation, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", commitCreation, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkingTreeViewModel", commitCreation, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryOperationsViewModel", commitCreation, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryOperationsAndInteractiveRebaseOwnTheirFeaturePresentationState()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var viewModels = Path.Combine(presentation, "ViewModels");
        var openRepository = File.ReadAllText(Path.Combine(viewModels, "OpenRepositoryViewModel.cs"));
        var operations = File.ReadAllText(Path.Combine(viewModels, "RepositoryOperationsViewModel.cs"));
        var rebase = File.ReadAllText(Path.Combine(viewModels, "InteractiveRebaseViewModel.cs"));
        var operationsContext = File.ReadAllText(
            Path.Combine(viewModels, "OpenRepositoryViewModel.RepositoryOperations.cs"));
        var rebaseContext = File.ReadAllText(
            Path.Combine(viewModels, "OpenRepositoryViewModel.InteractiveRebaseContext.cs"));
        var historyRewrite = File.ReadAllText(
            Path.Combine(viewModels, "OpenRepositoryViewModel.HistoryRewrite.cs"));
        var mainXaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var operationBanner = File.ReadAllText(
            Path.Combine(presentation, "Controls", "OperationBanner.xaml"));
        var mainPage = string.Join(
            Environment.NewLine,
            Directory.GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));

        Assert.Contains("public sealed class RepositoryOperationsViewModel", operations, StringComparison.Ordinal);
        Assert.Contains("IMergeService", operations, StringComparison.Ordinal);
        Assert.Contains("IConflictResolutionService", operations, StringComparison.Ordinal);
        Assert.Contains("IRepositoryOperationService", operations, StringComparison.Ordinal);
        Assert.Contains("IExternalGitToolService", operations, StringComparison.Ordinal);

        Assert.Contains("public sealed class InteractiveRebaseViewModel", rebase, StringComparison.Ordinal);
        Assert.Contains("IInteractiveRebaseService", rebase, StringComparison.Ordinal);
        Assert.Contains("IRepositoryIdentityService", rebase, StringComparison.Ordinal);
        Assert.Contains("IInteractiveRebaseAuthorChangeService", rebase, StringComparison.Ordinal);
        Assert.Contains("ICommitAuthorDateReader", rebase, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", rebase, StringComparison.Ordinal);

        Assert.Contains("public RepositoryOperationsViewModel RepositoryOperations { get; }", openRepository, StringComparison.Ordinal);
        Assert.Contains("public InteractiveRebaseViewModel InteractiveRebase { get; }", openRepository, StringComparison.Ordinal);
        Assert.Contains("IRepositoryOperationsContext", operationsContext, StringComparison.Ordinal);
        Assert.Contains("IInteractiveRebaseRepositoryContext", rebaseContext, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient<RepositoryOperationsViewModel>();", app, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient<InteractiveRebaseViewModel>();", app, StringComparison.Ordinal);
        Assert.Contains("RepositoryOperations.ApplyRepositoryState(", openRepository, StringComparison.Ordinal);
        Assert.Contains("InteractiveRebase.Invalidate();", historyRewrite, StringComparison.Ordinal);
        Assert.Contains("PublishOperationMessage", rebase, StringComparison.Ordinal);
        Assert.Contains("RepositoryOperations.SetOperationDisplay(message)", rebaseContext, StringComparison.Ordinal);

        foreach (var dependency in new[]
                 {
                     "IMergeService",
                     "IConflictResolutionService",
                     "IRepositoryOperationService",
                     "IExternalGitToolService",
                     "IRepositoryIdentityService",
                     "IInteractiveRebaseAuthorChangeService",
                     "ICommitAuthorDateReader",
                     "IInteractiveRebaseService"
                 })
        {
            Assert.DoesNotContain(dependency, openRepository, StringComparison.Ordinal);
        }

        foreach (var forwardingState in new[]
                 {
                     "public RepositoryOperation CurrentOperation",
                     "public RepositoryOperationState OperationState",
                     "public ConflictFile? SelectedConflict",
                     "public ObservableCollection<ConflictFile> Conflicts",
                     "public GitBranch? SelectedMergeBranch",
                     "public string OperationDisplay",
                     "public string RebaseOnto",
                     "public string RebaseTodoText"
                 })
        {
            Assert.DoesNotContain(forwardingState, openRepository, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(
            Path.Combine(viewModels, "OpenRepositoryViewModel.InteractiveRebase.cs")));
        Assert.DoesNotContain("ContinueRebaseCommand", openRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("AbortRebaseCommand", openRepository, StringComparison.Ordinal);

        Assert.Contains("DataContext=\"{Binding RepositoryOperations}\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InteractiveRebase.RebaseOnto}\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InteractiveRebase.RebaseTodoText, Mode=TwoWay}\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("OperationDisplay", operationBanner, StringComparison.Ordinal);
        Assert.Contains("ContinueOperationCommand", operationBanner, StringComparison.Ordinal);
        Assert.Contains("SelectedConflict", operationBanner, StringComparison.Ordinal);

        Assert.DoesNotContain("IInteractiveRebaseService", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositoryIdentityService", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("IInteractiveRebaseAuthorChangeService", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommitAuthorDateReader", mainPage, StringComparison.Ordinal);
        Assert.Contains("_viewModel.InteractiveRebase.", mainPage, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RepositoryOperations.", mainPage, StringComparison.Ordinal);
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
