using System.Xml.Linq;

namespace CSharpGit.Application.Tests;

public sealed class DesktopUiContractTests
{
    [Fact]
    public void MainWindowKeepsLaunchLayoutScrollingClippingAndBusyContracts()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var operationBanner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "OperationBanner.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var program = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Platforms", "Desktop", "Program.cs"));
        var splitter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GridSplitter.cs"));

        Assert.Contains("OpenRepositoryCommand", xaml);
        Assert.Contains("HistoryFilter", xaml);
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("UnoPlatformHostBuilder.Create", program);
        Assert.Contains("host.RunAsync()", program);
        Assert.Contains(".UseWin32(", program);
        Assert.Contains("Win32RenderingBackend.Vulkan", program);
        Assert.All(new[] { ".UseMacOS()", ".UseX11()" }, platform => Assert.Contains(platform, program));
        Assert.True(Count(xaml, "GridSplitter") >= 3, "All principal panes must remain resizable.");
        Assert.Contains("UserControl", splitter);
        Assert.Contains("HasVisualSurfaceForCheck", splitter);
        Assert.Contains("ResizeCompleted", splitter);
        Assert.Contains("layout.json", splitter);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml);
        Assert.Contains("MaxHeight=", xaml);
        Assert.Contains("IsActive=\"{Binding IsBusy}\"", xaml);
        Assert.Contains("Opening repository…", xaml);
        Assert.Contains("OperationBanner", xaml);
        Assert.Contains("OperationDisplay", operationBanner);
        Assert.Contains("!IsBusy", viewModel);
        Assert.Contains("SemaphoreSlim", viewModel);
        Assert.Contains("RaiseCanExecuteChanged", viewModel);
    }

    [Fact]
    public void GlobalThemeLivesInSettingsAndIsAppliedBeforeActivation()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var dialogs = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Dialogs.cs"));
        var settingsWindow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Settings.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var settingsPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml.cs"));
        var settingsViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "SettingsViewModel.cs"));
        var repositoryViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var themeManager = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ApplicationThemeManager.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var applicationContract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));
        var infrastructure = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "JsonAppSettingsService.cs"));

        Assert.DoesNotContain("AppearanceDialog", mainXaml);
        Assert.DoesNotContain("AppearanceDialog", dialogs);
        Assert.DoesNotContain("Appearance_Click", mainPage);
        Assert.DoesNotContain("Appearance…", mainXaml);
        Assert.DoesNotContain("RequestedTheme=\"{Binding SelectedTheme", mainXaml);
        Assert.DoesNotContain("RequestedTheme = RootLayout.RequestedTheme", settingsWindow);

        Assert.Contains("Appearance", settingsXaml);
        Assert.Contains("ThemeModeComboBox", settingsXaml);
        Assert.True(
            settingsXaml.IndexOf("Appearance", StringComparison.Ordinal) < settingsXaml.IndexOf("Commit time display", StringComparison.Ordinal),
            "Appearance must be shown before commit-time settings.");
        Assert.All(new[] { "ApplicationThemeMode.System, \"System\"", "ApplicationThemeMode.Light, \"Light\"", "ApplicationThemeMode.Dark, \"Dark\"" },
            option => Assert.Contains(option, settingsViewModel));
        Assert.Contains("ApplyThemeModeAsync", settingsPage);
        Assert.Contains("ShowSettingsError(exception)", settingsPage);
        Assert.Contains("Could not save settings", settingsXaml);
        Assert.Contains("new(AppSettingsContext.Current)", settingsPage);
        Assert.DoesNotContain("AppSettingsContext.Current", settingsViewModel);

        Assert.DoesNotContain("SelectedTheme", repositoryViewModel);
        Assert.DoesNotContain("SelectedThemeName", repositoryViewModel);
        Assert.DoesNotContain("IReadOnlyList<UiChoice<ElementTheme>> Themes", repositoryViewModel);
        Assert.DoesNotContain("ElementTheme", repositoryViewModel);

        Assert.Contains("ApplicationThemeMode ThemeMode", applicationContract);
        Assert.Contains("SetThemeModeAsync", applicationContract);
        Assert.Contains("ApplicationThemeManager", app);
        Assert.Contains("IAppSettingsService _settings", themeManager);
        Assert.Contains("_settings.Changed += Settings_Changed", themeManager);
        Assert.Contains("_settings.Changed -= Settings_Changed", themeManager);
        Assert.Contains("ApplicationThemeMode.System => ElementTheme.Default", themeManager);
        Assert.Contains("ApplicationThemeMode.Light => ElementTheme.Light", themeManager);
        Assert.Contains("ApplicationThemeMode.Dark => ElementTheme.Dark", themeManager);
        Assert.Contains("ApplyCurrentTheme(root);", themeManager);
        Assert.Contains("ToElementTheme(_settings.ThemeMode)", themeManager);
        Assert.Contains("root.DispatcherQueue", themeManager);
        Assert.Contains("root.RequestedTheme != requestedTheme", themeManager);
        Assert.Contains("Unregister(root)", themeManager);
        Assert.Contains("themeManager.Register(page)", settingsWindow);

        Assert.Contains("INotifyPropertyChanged", settingsViewModel);
        Assert.Contains("_settings.Changed += Settings_Changed", settingsViewModel);
        Assert.Contains("_settings.Changed -= Settings_Changed", settingsViewModel);

        var registrationIndex = app.IndexOf("_mainThemeRegistration = themeManager.Register(mainPage)", StringComparison.Ordinal);
        var contentIndex = app.IndexOf("_window.Content = mainPage", StringComparison.Ordinal);
        var activationIndex = app.LastIndexOf("_window.Activate();", StringComparison.Ordinal);
        Assert.True(registrationIndex >= 0 && registrationIndex < contentIndex && contentIndex < activationIndex,
            "Main root theme registration must happen before content assignment and Window.Activate().");

        Assert.DoesNotContain("Microsoft.UI.Xaml", applicationContract);
        Assert.DoesNotContain("ElementTheme", applicationContract);
        Assert.DoesNotContain("Microsoft.UI.Xaml", infrastructure);
        Assert.DoesNotContain("ElementTheme", infrastructure);
    }

    [Fact]
    public void MainWindowExposesAllOperationActionsAndStateDependentCommands()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var operationBanner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "OperationBanner.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var forcePushPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ForcePush.cs"));
        var surface = xaml + operationBanner + page + forcePushPage;
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        foreach (var command in new[]
        {
            "StageSelectedCommand", "StageAllCommand", "UnstageSelectedCommand", "UnstageAllCommand",
            "CommitCommand", "AmendCommand", "ConfirmDiscardCommand",
            "FetchCommand", "FetchAllCommand", "PullCommand", "CreateStashCommand", "ApplyStashCommand",
            "PopStashCommand", "MergeCommand", "StartRebaseCommand", "ContinueOperationCommand", "SkipOperationCommand",
            "AbortOperationCommand", "MergeToolCommand", "MergeToolWorkflowCommand"
        }) Assert.Contains(command, surface);

        Assert.Contains("Push_Click", surface);
        Assert.Contains("_referenceService.PushAsync", forcePushPage);
        Assert.Contains("Force push with lease…", surface);
        Assert.Contains("ForcePushWithLeaseAsync(repository, snapshot)", forcePushPage);
        Assert.Equal(2, Count(xaml, "IsEnabled=\"{Binding CanForcePushWithLease}\""));
        Assert.Contains("CanForcePushWithLease => Repository is not null && !IsBusy", viewModel);
        Assert.Contains("CurrentOperation == RepositoryOperation.None", viewModel);
        Assert.Contains("LocalBranches.Any(branch => branch.IsCurrent)", viewModel);

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
