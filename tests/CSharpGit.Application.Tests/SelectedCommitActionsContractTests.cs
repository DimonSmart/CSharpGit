namespace CSharpGit.Application.Tests;

public sealed class SelectedCommitActionsContractTests
{
    [Fact]
    public void RefreshKeepsRepositoryIdentityAndWorkingTreeMutationsSkipHistory()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeViewModel.cs"));
        var contextBridge = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.WorkingTree.cs"));

        Assert.DoesNotContain("_repositoryService.OpenAsync(Repository.WorkingDirectory)", viewModel);
        Assert.Contains("RefreshStateAsync(bool includeHistory)", viewModel);
        Assert.Contains("if (includeHistory)", viewModel);
        Assert.Contains("StageFileAsync", ExtractMethod(workingTree, "StageActiveAsync"));
        Assert.Contains("UnstageFileAsync", ExtractMethod(workingTree, "UnstageActiveAsync"));
        Assert.Contains("StageChangesAsync", ExtractMethod(workingTree, "StageSelectedAsync"));
        Assert.Contains("UnstageChangesAsync", ExtractMethod(workingTree, "UnstageSelectedAsync"));
        Assert.Contains("includeHistory: false", contextBridge);
        Assert.DoesNotContain("ClearPresentationSelection", ExtractMethod(workingTree, "StageActiveAsync"));
        Assert.DoesNotContain("ClearPresentationSelection", ExtractMethod(workingTree, "UnstageActiveAsync"));
    }

    [Fact]
    public void RepositoryStateApplicationIsBulkAndPresentationRebuildIsCoalesced()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var bulk = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "BulkObservableCollection.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var coalescing = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryRefresh.cs"));

        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeViewModel.cs"));
        Assert.Contains("WorkingTree.ApplyRepositoryState(repository, state.Changes)", viewModel);
        Assert.Contains("ReplaceAll(snapshot)", workingTree);
        Assert.Contains("NotifyCollectionChangedAction.Reset", bulk);
        Assert.Contains("QueueRepositoryPresentationRefresh(workingTreeChanged: true)", page);
        Assert.Contains("DispatcherQueue.TryEnqueue(FlushRepositoryPresentationRefresh)", coalescing);
    }

    [Fact]
    public void CommitMenuExposesRequiredActionsAndMainlineChoice()
    {
        var root = FindRepositoryRoot();
        var actions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitActions.cs"));
        var commitActions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CommitActionsViewModel.cs"));
        var contextAdapter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.CommitActions.cs"));

        foreach (var label in new[] { "Copy hash", "Create branch here…", "Checkout this commit", "Cherry-pick", "Revert", "Edit commit message…", "Fixup into previous commit", "Interactive rebase from here…", "Reset current branch to here", "Soft…", "Mixed…", "Hard…" })
            Assert.Contains(label, actions);

        Assert.Contains("HistoryList.RightTapped", actions);
        Assert.Contains("commit.Parents.Count > 1", actions);
        Assert.Contains("Mainline parent", actions);
        Assert.Contains("Uncommitted tracked changes will be lost", actions);
        Assert.Contains("Untracked files will not be deleted", actions);
        var fixup = ExtractMethod(actions, "FixupIntoPreviousCommit_Click");
        Assert.Contains("_viewModel.CommitActions.FixupAsync", fixup);
        Assert.DoesNotContain("FixupIntoPreviousCommitAsync", fixup);
        Assert.Contains("_commitActionService.FixupIntoPreviousCommitAsync", commitActions);
        Assert.Contains("RunCommitHistoryRewriteMutationAsync", commitActions);
        Assert.Contains("RunHistoryRewriteMutationAsync", contextAdapter);
        Assert.DoesNotContain("ContentDialog", fixup);
        Assert.DoesNotContain("ShowInteractiveRebaseEditorAsync", fixup);
    }

    [Fact]
    public void InteractiveRebaseUsesRawTodoAndDedicatedEditor()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml"));
        var actions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitActions.cs"));
        var dialogs = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Dialogs.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var rebaseViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "InteractiveRebaseViewModel.cs"));
        var rebase = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IInteractiveRebaseService.cs"));
        var rebaseImplementation = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitInteractiveRebaseService.cs"));
        var todoHelp = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "InteractiveRebaseTodoHelp.cs"));
        var editorXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "InteractiveRebaseTodoEditor.xaml"));
        var editorCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "InteractiveRebaseTodoEditor.xaml.cs"));

        Assert.Contains("Interactive rebase from here…", actions);
        Assert.Contains("HistoryList.RightTapped += HistoryList_RightTapped", actions);
        Assert.Contains("var hash = commit.Hash;", actions);
        Assert.Contains("PrepareInteractiveRebaseFromCommitAsync(hash)", actions);
        Assert.Contains("var hasLocalBranch = _viewModel.CurrentBranchName is not null;", actions);
        Assert.DoesNotContain("_workflowService", ExtractMethod(actions, "InteractiveRebaseFromHere_Click"));
        Assert.DoesNotContain("^", ExtractMethod(actions, "InteractiveRebaseFromHere_Click"));

        Assert.Contains("ReadInteractiveRebaseTodoAsync", rebase);
        Assert.Contains("ReadInteractiveRebaseTodoFromCommitAsync", rebase);
        Assert.Contains("StartInteractiveRebaseTodoAsync", rebase);
        Assert.Contains("ReadInteractiveRebaseTodoFromCommitAsync", rebaseViewModel);
        Assert.Contains("StartInteractiveRebaseTodoAsync", rebaseViewModel);
        Assert.DoesNotContain("ReadInteractiveRebasePlan", rebaseViewModel);

        Assert.Contains("public InteractiveRebaseViewModel InteractiveRebase { get; }", viewModel);
        Assert.Contains("InteractiveRebaseTodo", rebaseViewModel);
        Assert.Contains("RebaseTodoText", rebaseViewModel);
        Assert.DoesNotContain("RebasePlanItem", viewModel);
        Assert.DoesNotContain("LoadRebasePlanCommand", viewModel);
        Assert.DoesNotContain("StartRebaseCommand", viewModel);
        Assert.DoesNotContain("MoveRebaseUpCommand", viewModel);
        Assert.DoesNotContain("ApplyRebaseItemCommand", viewModel);

        Assert.DoesNotContain("InteractiveRebaseSection", xaml);
        Assert.DoesNotContain("Open interactive rebase…", xaml);
        Assert.Contains("InteractiveRebaseDialog", xaml);
        Assert.Contains("<controls:InteractiveRebaseTodoEditor", xaml);
        Assert.Contains("MaxWidth=\"1200\"", xaml);
        Assert.Contains("PrimaryButtonText=\"Start rebase\"", xaml);
        Assert.Contains("CloseButtonText=\"Cancel\"", xaml);
        Assert.DoesNotContain("Git interprets this todo directly", xaml);
        Assert.DoesNotContain("InteractiveRebasePlanList", xaml);
        Assert.DoesNotContain("RebaseActions", xaml);
        Assert.DoesNotContain("RebaseMessage", xaml);
        Assert.DoesNotContain("<StackPanel Width=\"620\"", xaml);

        Assert.Contains("LineNumberGutter", editorXaml);
        Assert.Contains("ExecutableTextOverlay", editorXaml);
        Assert.Contains("AcceptsReturn=\"True\"", editorXaml);
        Assert.Contains("TextWrapping=\"NoWrap\"", editorXaml);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"", editorXaml);
        Assert.Contains("TextFillColorSecondaryBrush", editorXaml);
        Assert.Contains("IsCommentLine", editorCode);
        Assert.Contains("ViewChanged", editorCode);
        Assert.Contains("ChangeView", editorCode);

        foreach (var command in new[] { "# p, pick", "# r, reword", "# e, edit", "# s, squash", "# f, fixup", "# x, exec", "# b, break", "# d, drop" })
            Assert.Contains(command, todoHelp);
        Assert.Contains("InteractiveRebaseTodoHelp.Text", rebaseImplementation);

        Assert.Contains("<x:Double x:Key=\"ContentDialogMaxWidth\">1200</x:Double>", appXaml);
        Assert.Contains("<x:Double x:Key=\"SimpleContentDialogMaxWidth\">1200</x:Double>", appXaml);
        Assert.Contains("sender.XamlRoot?.Size", dialogs);
        Assert.DoesNotContain("ActualWidth - 96d", dialogs);
        Assert.DoesNotContain("ActualHeight - 220d", dialogs);

        Assert.DoesNotContain("OpenInteractiveRebase" + "_Click", dialogs);
        Assert.DoesNotContain("GitOperations" + "Dialog", dialogs);
        Assert.DoesNotContain("PrepareInteractiveRebase" + "Async()", rebaseViewModel);
        Assert.Contains("InteractiveRebaseDialog", dialogs);
        Assert.Contains("ShowInteractiveRebaseEditorAsync", dialogs);
        Assert.Contains("1200d", dialogs);
        Assert.Contains("StartPreparedInteractiveRebaseAsync", dialogs);
    }

    [Fact]
    public void CancelCommitRemainsPresentationOnly()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CommitCreationViewModel.cs"));
        var method = ExtractMethod(viewModel, "CancelCommitAsync");
        var closeChoice = ExtractMethod(viewModel, "CloseEmptyIndexChoice");

        Assert.Contains("CloseEmptyIndexChoice();", method);
        Assert.Contains("IsEmptyIndexChoiceOpen = false", closeChoice);
        Assert.DoesNotContain("_workingTreeService", method);
        Assert.DoesNotContain("RunCommitMutationAsync", method);
        Assert.DoesNotContain("CommitMessage =", method);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method {methodName} was not found.");
        start = source.LastIndexOf('\n', start) + 1;
        var next = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        if (next < 0) next = source.IndexOf("\n    internal ", start + 1, StringComparison.Ordinal);
        if (next < 0) next = source.Length;
        return source[start..next];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
