namespace CSharpGit.Desktop.Tests;

public sealed class SettingsDiagnosticsUiContractTests
{
    [Fact]
    public void DiagnosticsShowsSessionLogPathAndActions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsWindowController.cs"));

        Assert.Contains("x:Name=\"LogFilePathText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Copy path\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open folder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabled=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LogFilePathText.Text = _logFilePath;", page, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetContent(package);", page, StringComparison.Ordinal);
        Assert.Contains("_desktopShellService.OpenFolderAsync(directory)", page, StringComparison.Ordinal);
        Assert.Contains("SessionFileLoggerProvider.CurrentLogPath", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsExposesIndependentHistoryPerformanceCaptureSetting()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var settings = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));

        Assert.Contains("Enable history performance diagnostics", xaml, StringComparison.Ordinal);
        Assert.Contains("HistoryPerformanceDiagnosticsEnabled", settings, StringComparison.Ordinal);
        Assert.Contains("History performance capture", xaml, StringComparison.Ordinal);
        Assert.Contains("independent of normal file logging", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenFolderUsesCrossPlatformDesktopShell()
    {
        var root = FindRepositoryRoot();
        var contracts = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IRepositoryFileVersionService.cs"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "DesktopFileServices.cs"));

        Assert.Contains("Task OpenFolderAsync(", contracts, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsWindows()", shell, StringComparison.Ordinal);
        Assert.Contains("CreateCommand(\"explorer.exe\", fullPath)", shell, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsMacOS()", shell, StringComparison.Ordinal);
        Assert.Contains("CreateCommand(\"open\", fullPath)", shell, StringComparison.Ordinal);
        Assert.Contains("CreateCommand(\"xdg-open\", fullPath)", shell, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
