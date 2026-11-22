namespace CSharpGit.Application.Tests;

public sealed class AboutUpdateUiContractTests
{
    [Fact]
    public void AboutIsAvailableFromRepositoryAndStartScreen()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.xaml"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "StartScreenView.xaml"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryToolbarView.xaml"));

        Assert.True(Count(xaml, "About CSharpGit…") >= 2);
        Assert.Contains("StartScreenOverflowButton", xaml);
        Assert.Contains("ConverterParameter=Invert", xaml);
        Assert.Contains("Settings…", xaml);
        Assert.DoesNotContain("More Git " + "operations…", xaml, StringComparison.Ordinal);

        var separatorIndex = xaml.IndexOf("<MenuFlyoutSeparator />", StringComparison.Ordinal);
        var settingsIndex = xaml.IndexOf("Text=\"Settings…\"", separatorIndex, StringComparison.Ordinal);
        var aboutIndex = xaml.IndexOf("Text=\"About CSharpGit…\"", settingsIndex, StringComparison.Ordinal);
        Assert.True(separatorIndex >= 0 && settingsIndex > separatorIndex && aboutIndex > settingsIndex);
    }

    [Fact]
    public void AboutChecksUpdatesAndKeepsPlatformSpecificInstallationOutsidePresentation()
    {
        var root = FindRepositoryRoot();
        var about = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "Dialogs", "AboutUpdateDialogController.cs"));
        var app = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var infrastructure = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Infrastructure.Core", "ApplicationUpdateServices.cs"));

        Assert.Contains("Checking for updates...", about);
        Assert.Contains("You are up to date.", about);
        Assert.Contains("New version", about);
        Assert.Contains("Unable to check for updates.", about);
        Assert.Contains("Update now", about);
        Assert.Contains("Open release page", about);
        Assert.Contains("_runUiOperation(CheckUpdateOnDialogOpenAsync", about);
        Assert.Contains("dialogLifetime.IsCancellationRequested || _isShuttingDown()", about);
        Assert.DoesNotContain("async void Dialog_Opened", about);
        Assert.Contains("dialog.Opened -= Dialog_Opened", about);
        Assert.Contains("_applicationVersionProvider.DisplayVersion", about);
        Assert.Contains("_isHistoryRewriteInProgress()", about);
        Assert.Contains("_isRepositoryMaintenanceInProgress()", about);
        Assert.Contains("_confirmCloseAsync()", about);
        Assert.Contains("_completeRestart()", about);
        Assert.Contains("CompleteApplicationUpdateRestart", app);
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.About.cs")));

        Assert.Contains("GitHubUpdateCheckService", app);
        Assert.Contains("MacOsHomebrewUpdateInstaller", app);
        Assert.Contains("UnsupportedApplicationUpdateInstaller", app);
        var close = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Windowing", "WindowCloseOrchestrator.cs"));
        var safety = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Windowing", "WindowCloseSafetyProvider.cs"));
        Assert.Contains("session.IsUnsafeToClose", close);
        Assert.Contains("IsUpdateRunning: updateActivity.IsRunning", safety);
        Assert.Contains("Stage != WindowCloseStage.Idle", close);
        Assert.Contains("ScheduleShutdown();", close);
        Assert.Contains("session.PrepareForShutdownAsync(CancellationToken.None)", close);
        Assert.Contains("session.CompleteShutdown()", close);

        Assert.DoesNotContain("brew update", about, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("brew upgrade", about, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://api.github.com/repos/DimonSmart/CSharpGit/releases/latest", infrastructure);
        Assert.Contains("\"update\"", infrastructure);
        Assert.Contains("\"info\", \"--cask\", \"--json=v2\"", infrastructure);
        Assert.Contains("\"upgrade\", \"--cask\", \"--no-quit\", \"--appdir=/Applications\"", infrastructure);
        Assert.Contains("/Applications/CSharpGit.app/Contents/MacOS/", infrastructure);
        Assert.Contains("CFBundleShortVersionString", infrastructure);
        Assert.Contains("/usr/bin/xattr", infrastructure);
        Assert.Contains("/usr/bin/open", infrastructure);
        Assert.Contains("CancellationToken.None", infrastructure);
        Assert.DoesNotContain("sh -c", infrastructure);
    }

    [Fact]
    public void IntentDocumentsDescribeAboutAndHomebrewOnlySelfUpdate()
    {
        var root = FindRepositoryRoot();
        var updateIntent = File.ReadAllText(Path.Combine(
            root, ".idd", "intent", "IDD-0036.spec-about-and-application-update.md"));
        var releaseIntent = File.ReadAllText(Path.Combine(
            root, ".idd", "intent", "IDD-0020.spec-release-distribution.md"));

        Assert.Contains("GitHub Release", updateIntent);
        Assert.Contains("Homebrew", updateIntent);
        Assert.Contains("/Applications/CSharpGit.app", updateIntent);
        Assert.Contains("safe restart", updateIntent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IDD-0036", releaseIntent);
        Assert.DoesNotContain(
            "встроенный auto-update не являются частью текущего поведения",
            releaseIntent);
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length)
        / fragment.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
