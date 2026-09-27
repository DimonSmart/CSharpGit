namespace CSharpGit.Application.Tests;

public sealed class AboutUpdateUiContractTests
{
    [Fact]
    public void AboutIsAvailableFromRepositoryAndStartScreen()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.xaml"));

        Assert.True(Count(xaml, "About CSharpGit…") >= 2);
        Assert.Contains("StartScreenOverflowButton", xaml);
        Assert.Contains("ConverterParameter=Invert", xaml);
        Assert.Contains("Settings…", xaml);
        Assert.Contains("More Git operations…", xaml);

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
            root, "src", "CSharpGit.Presentation", "MainPage.About.cs"));
        var app = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var infrastructure = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Infrastructure", "ApplicationUpdateServices.cs"));

        Assert.Contains("Checking for updates...", about);
        Assert.Contains("You are up to date.", about);
        Assert.Contains("New version", about);
        Assert.Contains("Unable to check for updates.", about);
        Assert.Contains("Update now", about);
        Assert.Contains("Open release page", about);
        Assert.Contains("_applicationVersionProvider.DisplayVersion", about);
        Assert.Contains("IsHistoryRewriteInProgress", about);
        Assert.Contains("IsRepositoryMaintenanceInProgress", about);
        Assert.Contains("ConfirmCloseAsync()", about);
        Assert.Contains("CompleteApplicationUpdateRestart", about);

        Assert.Contains("GitHubUpdateCheckService", app);
        Assert.Contains("MacOsHomebrewUpdateInstaller", app);
        Assert.Contains("UnsupportedApplicationUpdateInstaller", app);
        Assert.Contains("IsApplicationUpdateInProgress", app);
        Assert.Contains("_closeConfirmed = true", app);
        Assert.Contains("page?.BeginShutdown()", app);

        Assert.DoesNotContain("brew ", about, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains("GitHub Releases", updateIntent);
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
