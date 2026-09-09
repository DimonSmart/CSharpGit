namespace CSharpGit.Application.Tests;

public sealed class SettingsLoggingContractTests
{
    [Fact]
    public void SettingsExposePersistentOptionalLoggingWithoutGraphTracingLayer()
    {
        var root = FindRepositoryRoot();
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var settingsContract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));
        var settingsPersistence = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "JsonAppSettingsService.cs"));
        var logger = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Diagnostics", "SessionFileLoggerProvider.cs"));
        var graphControl = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphControl.cs"));
        var graphConverter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitTopologyToGraphVisualConverter.cs"));

        Assert.Contains("Diagnostics", settingsXaml);
        Assert.Contains("LoggingToggle", settingsXaml);
        Assert.Contains("LogLevelComboBox", settingsXaml);
        Assert.Contains("ApplicationLogLevel", settingsContract);
        Assert.Contains("LoggingEnabled", settingsContract);
        Assert.Contains("SetLoggingSettingsAsync", settingsContract);
        Assert.Contains("ApplicationLogLevel.Information", settingsPersistence);
        Assert.Contains("LoggingEnabled = _loggingEnabled", settingsPersistence);
        Assert.Contains("public void Configure(bool enabled, LogLevel minimumLevel)", logger);
        Assert.Contains("Directory.CreateDirectory(_directory)", logger);
        Assert.DoesNotContain("CommitGraphDiagnostics", graphControl);
        Assert.DoesNotContain("CommitGraphDiagnostics", graphConverter);
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "Diagnostics", "CommitGraphDiagnostics.cs")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
