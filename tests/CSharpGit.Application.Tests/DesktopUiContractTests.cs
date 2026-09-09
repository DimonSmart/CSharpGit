using System.Xml.Linq;

namespace CSharpGit.Application.Tests;

public sealed class DesktopUiContractTests
{
    [Fact]
    public void MainWindowKeepsLaunchLayoutScrollingClippingThemeAndBusyContracts()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var operationBanner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "OperationBanner.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var program = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Platforms", "Desktop", "Program.cs"));
        var splitter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GridSplitter.cs"));

        Assert.Contains("OpenRepositoryCommand", xaml);
        Assert.Contains("UnoPlatformHostBuilder.Create", program);
        Assert.Contains("host.RunAsync()", program);
        Assert.All(new[] { ".UseWin32()", ".UseMacOS()", ".UseX11()" }, platform => Assert.Contains(platform, program));
        Assert.True(Count(xaml, "GridSplitter") >= 3, "All principal panes must remain resizable.");
        Assert.Contains("UserControl", splitter);
        Assert.Contains("HasVisualSurfaceForCheck", splitter);
        Assert.Contains("ResizeCompleted", splitter);
        Assert.Contains("layout.json", splitter);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml);
        Assert.Contains("MaxHeight=", xaml);
        Assert.Contains("RequestedTheme=\"{Binding SelectedTheme", xaml);
        Assert.All(new[] { "System", "Light", "Dark" }, theme => Assert.Contains(theme, viewModel));
        Assert.Contains("IsActive=\"{Binding IsBusy}\"", xaml);
        Assert.Contains("Opening repository…", xaml);
        Assert.Contains("OperationBanner", xaml);
        Assert.Contains("OperationDisplay", operationBanner);
        Assert.Contains("!IsBusy", viewModel);
        Assert.Contains("SemaphoreSlim", viewModel);
        Assert.Contains("RaiseCanExecuteChanged", viewModel);
    }

    [Fact]
    public void MainWindowExposesAllOperationActionsAndStateDependentCommands()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var operationBanner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "OperationBanner.xaml"));
        var surface = xaml + operationBanner;
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        foreach (var binding in new[]
        {
            "StageCommand", "UnstageCommand", "CommitCommand", "AmendCommand", "ConfirmDiscardCommand",
            "FetchCommand", "FetchAllCommand", "PullCommand", "PushCommand", "CreateStashCommand", "ApplyStashCommand",
            "PopStashCommand", "MergeCommand", "StartRebaseCommand", "ContinueOperationCommand", "SkipOperationCommand",
            "AbortOperationCommand", "MergeToolCommand", "MergeToolWorkflowCommand"
        }) Assert.Contains($"Command=\"{{Binding {binding}}}\"", surface);

        Assert.Contains("OperationState.CanContinue", viewModel);
        Assert.Contains("OperationState.CanSkip", viewModel);
        Assert.Contains("OperationState.CanAbort", viewModel);
        Assert.Contains("SelectedConflict?.CanRunMergeTool", viewModel);
    }

    [Fact]
    public void CommitWorkflowRequiresExplicitEmptyIndexChoiceAndProtectsDraftOnClose()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var program = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Platforms", "Desktop", "Program.cs"));

        Assert.All(new[] { "StageAllAndCommitCommand", "ConfirmEmptyCommitCommand", "CancelCommitCommand" }, command => Assert.Contains(command, xaml));
        Assert.Contains("!Changes.Any(change => change.IsStaged)", viewModel);
        Assert.Contains("StageAllAsync", viewModel);
        Assert.Contains("HasUnappliedCommitMessage", page);
        Assert.Contains("Keep editing", page);
        Assert.Contains("RequiresCloseConfirmation", lifecycle);
        Assert.Contains("!page.RequiresCloseConfirmation", app);
        Assert.Contains("eventArgs.Cancel = true", app);
        Assert.Contains("_closeConfirmationInProgress", app);
        Assert.Contains("page.DispatcherQueue.TryEnqueue", app);
        Assert.Contains("page?.BeginShutdown()", app);
        Assert.DoesNotContain("_window.Closed += async", app);
        Assert.Contains("application?.StopHost()", program);
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
