namespace CSharpGit.Application.Tests;

public sealed class GitConsoleUiContractTests
{
    [Fact]
    public void GitConsoleKeepsDiagnosticAndWorkflowContractsVisible()
    {
        var root = FindRepositoryRoot();
        var consoleXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml"));
        var consoleCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml.cs"));
        var integration = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.GitConsole.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var settingsViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "SettingsViewModel.cs"));
        var operationBanner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "OperationBanner.xaml"));

        Assert.Contains("Git Console", consoleXaml);
        Assert.Contains("CommandList", consoleXaml);
        Assert.Contains("StandardOutputText", consoleXaml);
        Assert.Contains("StandardErrorText", consoleXaml);
        Assert.Contains("FontFamily=\"Consolas\"", consoleXaml);
        Assert.Contains("Copy command", consoleXaml);
        Assert.Contains("Copy all", consoleXaml);
        Assert.Contains("No Git commands recorded in this session.", consoleXaml);
        Assert.Contains("User commands", consoleCode);
        Assert.Contains("All commands", consoleCode);
        Assert.DoesNotContain("Run again", consoleXaml, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("InitializeGitConsole", integration);
        Assert.Contains("GitConsoleAutoOpenMode.Always", integration);
        Assert.Contains("GitConsoleAutoOpenMode.OnErrors", integration);
        Assert.Contains("GitCommandKind.User", integration);
        Assert.Contains("ToggleGitConsoleFromStatus", integration);
        Assert.Contains("VirtualKey.Oem3", integration);
        Assert.Contains("ToolTipService.SetToolTip", integration);

        Assert.Contains("Git Console", settingsXaml);
        Assert.Contains("GitConsoleAutoOpenComboBox", settingsXaml);
        Assert.Contains("latest 100 Git commands", settingsXaml);
        Assert.All(new[] { "On errors", "Always", "Never" }, label => Assert.Contains(label, settingsViewModel));

        Assert.Contains("Continue", operationBanner);
        Assert.Contains("Abort", operationBanner);
        Assert.Contains("Skip", operationBanner);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
